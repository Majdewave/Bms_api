using System.Security.Claims;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services.Platform;

public interface IPlatformUserManagementService
{
    Task<PlatformUsersListResponseDto> GetUsersAsync(PlatformUsersQuery query, CancellationToken cancellationToken);
    Task<PlatformUserListItemDto> CreateUserAsync(PlatformUserCreateRequest request, Guid currentUserId, CancellationToken cancellationToken);
    Task<PlatformUserListItemDto?> UpdateUserAsync(Guid userId, PlatformUserUpdateRequest request, Guid currentUserId, CancellationToken cancellationToken);
    Task<PlatformUserListItemDto?> ResetPasswordAsync(Guid userId, PlatformUserPasswordResetRequest request, Guid currentUserId, CancellationToken cancellationToken);
    Task<PlatformUserListItemDto?> ForcePasswordResetAsync(Guid userId, Guid currentUserId, CancellationToken cancellationToken);
    Task<PlatformUserListItemDto?> SetEnabledAsync(Guid userId, bool isEnabled, Guid currentUserId, CancellationToken cancellationToken);
    Task<PlatformUserListItemDto?> ChangeRoleAsync(Guid userId, PlatformUserRoleRequest request, Guid currentUserId, CancellationToken cancellationToken);
    Task DeleteUserAsync(Guid userId, Guid currentUserId, CancellationToken cancellationToken);
}

public sealed class PlatformUserManagementService : IPlatformUserManagementService
{
    private readonly MasterDbContext _masterDb;

    public PlatformUserManagementService(MasterDbContext masterDb)
    {
        _masterDb = masterDb;
    }

    public async Task<PlatformUsersListResponseDto> GetUsersAsync(PlatformUsersQuery query, CancellationToken cancellationToken)
    {
        var normalized = NormalizeQuery(query);
        IQueryable<PlatformUser> userQuery = _masterDb.PlatformUsers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(normalized.Search))
        {
            var search = normalized.Search.Trim().ToLowerInvariant();
            userQuery = userQuery.Where(user =>
                user.FullName.ToLower().Contains(search) ||
                user.Email.ToLower().Contains(search));
        }

        if (TryParseRoleFilter(normalized.Role, out var roleFilter))
        {
            userQuery = userQuery.Where(user => user.Role == roleFilter);
        }

        if (TryParseStatusFilter(normalized.Status, out var statusFilter))
        {
            userQuery = statusFilter switch
            {
                "active" => userQuery.Where(user => user.IsActive),
                "disabled" => userQuery.Where(user => !user.IsActive),
                _ => userQuery
            };
        }

        userQuery = normalized.Sort?.ToLowerInvariant() switch
        {
            "name" => userQuery.OrderBy(user => user.FullName),
            "role" => userQuery.OrderBy(user => user.Role).ThenBy(user => user.FullName),
            _ => userQuery.OrderByDescending(user => user.CreatedAt)
        };

        var total = await userQuery.CountAsync(cancellationToken);
        var items = await userQuery
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .Select(user => MapUser(user))
            .ToListAsync(cancellationToken);

        var stats = await BuildStatsAsync(cancellationToken);

        return new PlatformUsersListResponseDto(items, total, normalized.Page, normalized.PageSize, stats);
    }

    public async Task<PlatformUserListItemDto> CreateUserAsync(PlatformUserCreateRequest request, Guid currentUserId, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (await _masterDb.PlatformUsers.AnyAsync(user => user.Email.ToLower() == email, cancellationToken))
        {
            throw new InvalidOperationException("A platform user with this email already exists.");
        }

        var now = DateTime.UtcNow;
        var entity = new PlatformUser
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = ParseRole(request.Role),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
            PasswordResetToken = request.ForcePasswordReset ? Guid.NewGuid().ToString("N") : null,
            PasswordResetExpiresAt = request.ForcePasswordReset ? now.AddDays(2) : null
        };

        if (entity.Role == PlatformRole.Owner && !entity.IsActive)
        {
            throw new InvalidOperationException("Super Admins must remain active.");
        }

        _masterDb.PlatformUsers.Add(entity);
        await _masterDb.SaveChangesAsync(cancellationToken);

        return MapUser(entity);
    }

    public async Task<PlatformUserListItemDto?> UpdateUserAsync(Guid userId, PlatformUserUpdateRequest request, Guid currentUserId, CancellationToken cancellationToken)
    {
        var user = await _masterDb.PlatformUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        if (currentUserId == userId && request.IsActive is false)
        {
            throw new InvalidOperationException("You cannot remove your own permissions.");
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = NormalizeEmail(request.Email);
            var emailExists = await _masterDb.PlatformUsers.AnyAsync(item => item.Id != userId && item.Email.ToLower() == email, cancellationToken);
            if (emailExists)
            {
                throw new InvalidOperationException("A platform user with this email already exists.");
            }
            user.Email = email;
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            user.FullName = request.FullName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var nextRole = ParseRole(request.Role);
            await EnsureLastOwnerRuleAsync(user, nextRole, cancellationToken);
            if (currentUserId == userId && nextRole != user.Role)
            {
                throw new InvalidOperationException("You cannot remove your own permissions.");
            }
            user.Role = nextRole;
        }

        if (request.IsActive.HasValue)
        {
            if (currentUserId == userId && request.IsActive == false)
            {
                throw new InvalidOperationException("You cannot remove your own permissions.");
            }

            if (!request.IsActive.Value)
            {
                await EnsureLastOwnerRuleAsync(user, user.Role, cancellationToken, disableRequested: true);
            }

            user.IsActive = request.IsActive.Value;
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _masterDb.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<PlatformUserListItemDto?> ResetPasswordAsync(Guid userId, PlatformUserPasswordResetRequest request, Guid currentUserId, CancellationToken cancellationToken)
    {
        var user = await _masterDb.PlatformUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _masterDb.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<PlatformUserListItemDto?> ForcePasswordResetAsync(Guid userId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var user = await _masterDb.PlatformUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        user.PasswordResetToken = Guid.NewGuid().ToString("N");
        user.PasswordResetExpiresAt = DateTime.UtcNow.AddDays(2);
        user.UpdatedAt = DateTime.UtcNow;
        await _masterDb.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<PlatformUserListItemDto?> SetEnabledAsync(Guid userId, bool isEnabled, Guid currentUserId, CancellationToken cancellationToken)
    {
        var user = await _masterDb.PlatformUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        if (currentUserId == userId && !isEnabled)
        {
            throw new InvalidOperationException("You cannot remove your own permissions.");
        }

        if (!isEnabled)
        {
            await EnsureLastOwnerRuleAsync(user, user.Role, cancellationToken, disableRequested: true);
        }

        user.IsActive = isEnabled;
        user.UpdatedAt = DateTime.UtcNow;
        await _masterDb.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<PlatformUserListItemDto?> ChangeRoleAsync(Guid userId, PlatformUserRoleRequest request, Guid currentUserId, CancellationToken cancellationToken)
    {
        var user = await _masterDb.PlatformUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        var nextRole = ParseRole(request.Role);
        if (currentUserId == userId && nextRole != user.Role)
        {
            throw new InvalidOperationException("You cannot remove your own permissions.");
        }

        await EnsureLastOwnerRuleAsync(user, nextRole, cancellationToken);
        user.Role = nextRole;
        user.UpdatedAt = DateTime.UtcNow;
        await _masterDb.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task DeleteUserAsync(Guid userId, Guid currentUserId, CancellationToken cancellationToken)
    {
        if (currentUserId == userId)
        {
            throw new InvalidOperationException("You cannot delete yourself.");
        }

        var user = await _masterDb.PlatformUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user == null)
        {
            return;
        }

        await EnsureLastOwnerRuleAsync(user, user.Role, cancellationToken, deleteRequested: true);
        _masterDb.PlatformUsers.Remove(user);
        await _masterDb.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureLastOwnerRuleAsync(
        PlatformUser targetUser,
        PlatformRole nextRole,
        CancellationToken cancellationToken,
        bool disableRequested = false,
        bool deleteRequested = false)
    {
        if (targetUser.Role != PlatformRole.Owner && nextRole != PlatformRole.Owner)
        {
            return;
        }

        var ownerCount = await _masterDb.PlatformUsers.CountAsync(user => user.Role == PlatformRole.Owner && user.IsActive, cancellationToken);
        var removingOwner = targetUser.Role == PlatformRole.Owner && (disableRequested || deleteRequested || nextRole != PlatformRole.Owner);

        if (removingOwner && ownerCount <= 1)
        {
            throw new InvalidOperationException("Cannot disable or remove the last Super Admin.");
        }
    }

    private async Task<PlatformUsersStatsDto> BuildStatsAsync(CancellationToken cancellationToken)
    {
        var users = await _masterDb.PlatformUsers.AsNoTracking().ToListAsync(cancellationToken);
        return new PlatformUsersStatsDto(
            users.Count,
            users.Count(user => user.IsActive),
            users.Count(user => !user.IsActive),
            users.Count(user => user.Role == PlatformRole.Owner),
            users.Count(user => user.Role == PlatformRole.Support));
    }

    private static PlatformUserListItemDto MapUser(PlatformUser user)
    {
        return new PlatformUserListItemDto(
            user.Id,
            user.FullName,
            user.Email,
            RoleLabel(user.Role),
            user.IsActive,
            user.LastLoginAt,
            user.CreatedAt,
            user.UpdatedAt,
            !string.IsNullOrWhiteSpace(user.PasswordResetToken) && user.PasswordResetExpiresAt.HasValue && user.PasswordResetExpiresAt > DateTime.UtcNow);
    }

    private static PlatformRole ParseRole(string role)
    {
        return role.Trim().ToLowerInvariant() switch
        {
            "owner" or "super admin" or "super_admin" => PlatformRole.Owner,
            "platformadmin" or "platform admin" or "admin" => PlatformRole.PlatformAdmin,
            "support" or "support staff" => PlatformRole.Support,
            _ => throw new InvalidOperationException("Invalid platform role.")
        };
    }

    private static string RoleLabel(PlatformRole role)
    {
        return role switch
        {
            PlatformRole.Owner => "Super Admin",
            PlatformRole.PlatformAdmin => "Platform Admin",
            PlatformRole.Support => "Support Staff",
            _ => role.ToString()
        };
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static bool TryParseRoleFilter(string? role, out PlatformRole value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(role) || string.Equals(role, "all", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            value = ParseRole(role);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseStatusFilter(string? status, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = status.Trim().ToLowerInvariant();
        return normalized is "active" or "disabled";
    }

    private static PlatformUsersQuery NormalizeQuery(PlatformUsersQuery query)
    {
        return query with
        {
            Page = query.Page < 1 ? 1 : query.Page,
            PageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 100),
            Search = query.Search?.Trim(),
            Role = query.Role?.Trim(),
            Status = query.Status?.Trim(),
            Sort = string.IsNullOrWhiteSpace(query.Sort) ? "newest" : query.Sort.Trim()
        };
    }
}
