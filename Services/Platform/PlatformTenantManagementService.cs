using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Clienta.Api.Services.Platform;

public sealed class PlatformTenantNotFoundException : Exception
{
    public PlatformTenantNotFoundException(Guid tenantId) : base($"Tenant '{tenantId}' was not found.")
    {
        TenantId = tenantId;
    }

    public Guid TenantId { get; }
}

public sealed class PlatformTenantConflictException : Exception
{
    public PlatformTenantConflictException(string message) : base(message)
    {
    }
}

public sealed class PlatformTenantValidationException : Exception
{
    public PlatformTenantValidationException(string message) : base(message)
    {
    }
}

public class PlatformTenantManagementService : IPlatformTenantManagementService
{
    private sealed record TenantUserLite(
        Guid TenantId,
        Guid Id,
        string Role,
        string? FullName,
        string Email,
        DateTime CreatedAt,
        DateTime? LastLoginAt);

    private readonly MasterDbContext _masterDb;
    private readonly AppDbContext _appDb;
    private readonly ITenantApprovalService _tenantApprovalService;

    public PlatformTenantManagementService(
        MasterDbContext masterDb,
        AppDbContext appDb,
        ITenantApprovalService tenantApprovalService)
    {
        _masterDb = masterDb;
        _appDb = appDb;
        _tenantApprovalService = tenantApprovalService;
    }

    public async Task<PlatformTenantsListResponseDto> GetTenantsAsync(PlatformTenantsQuery query, CancellationToken cancellationToken)
    {
        var normalized = NormalizeQuery(query);

        IQueryable<Tenant> tenantQuery = _masterDb.Tenants.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(normalized.SearchName))
        {
            tenantQuery = tenantQuery.Where(t => EF.Functions.ILike(t.Name, $"%{normalized.SearchName!.Trim()}%"));
        }

        if (TryParseBusinessPlan(normalized.Plan, out var planFilter))
        {
            tenantQuery = tenantQuery.Where(t => t.Plan == planFilter);
        }

        if (TryParseStatusFilter(normalized.Status, out var statusFilter))
        {
            tenantQuery = ApplyStatusFilter(tenantQuery, statusFilter);
        }

        if (TryParseRegistrationDateFilter(normalized.RegistrationDate, out var registrationDateFilter))
        {
            tenantQuery = ApplyRegistrationDateFilter(tenantQuery, registrationDateFilter);
        }

        if (TryParseBusinessTypeFilter(normalized.BusinessType, out var businessTypeFilter))
        {
            tenantQuery = ApplyBusinessTypeFilter(tenantQuery, businessTypeFilter);
        }

        if (TryParseTrialFilter(normalized.Trial, out var trialFilter))
        {
            tenantQuery = ApplyTrialFilter(tenantQuery, trialFilter);
        }

        if (!string.IsNullOrWhiteSpace(normalized.SearchOwner))
        {
            var ownerTerm = normalized.SearchOwner.Trim();

            var matchingTenantIds = await _appDb.Users
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(u =>
                    (!string.IsNullOrWhiteSpace(u.FullName) && EF.Functions.ILike(u.FullName, $"%{ownerTerm}%")) ||
                    EF.Functions.ILike(u.Email, $"%{ownerTerm}%"))
                .Select(u => u.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken);

            tenantQuery = tenantQuery.Where(t => matchingTenantIds.Contains(t.Id));
        }

        tenantQuery = ApplySort(tenantQuery, normalized.Sort);

        var total = await tenantQuery.CountAsync(cancellationToken);

        var pageTenants = await tenantQuery
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .ToListAsync(cancellationToken);

        var tenantIds = pageTenants.Select(t => t.Id).ToList();

        var users = await _appDb.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => tenantIds.Contains(u.TenantId))
            .Select(u => new TenantUserLite(
                u.TenantId,
                u.Id,
                u.Role,
                u.FullName,
                u.Email,
                u.CreatedAt,
                u.LastLoginAt))
            .ToListAsync(cancellationToken);

        var usersByTenant = users
            .GroupBy(u => u.TenantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var auditStatusByTenant = await _appDb.AuditLogs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => tenantIds.Contains(a.TenantId) &&
                        (a.ActionType == "Business Approved" ||
                         a.ActionType == "Business Rejected" ||
                         a.ActionType == "Business Under Review"))
            .GroupBy(a => a.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                LatestActionType = g
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => x.ActionType)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.TenantId, x => x.LatestActionType, cancellationToken);

        var items = new List<PlatformTenantListItemDto>(pageTenants.Count);
        foreach (var tenant in pageTenants)
        {
            usersByTenant.TryGetValue(tenant.Id, out var tenantUsers);
            tenantUsers ??= new List<TenantUserLite>();

            var owner = tenantUsers
                .OrderByDescending(u => tenant.OwnerUserId.HasValue && u.Id == tenant.OwnerUserId.Value)
                .ThenByDescending(u => string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                .ThenBy(u => u.CreatedAt)
                .FirstOrDefault();

            var lastLoginAt = tenantUsers
                .Where(u => u.LastLoginAt.HasValue)
                .Select(u => u.LastLoginAt)
                .OrderByDescending(v => v)
                .FirstOrDefault();

            var approvalStatus = ResolveApprovalStatus(tenant, auditStatusByTenant.TryGetValue(tenant.Id, out var auditStatus) ? auditStatus : null);
            var businessType = ResolveBusinessType(tenant);

            items.Add(new PlatformTenantListItemDto(
                tenant.Id,
                tenant.Name,
                NormalizeOptionalText(owner?.FullName),
                NormalizeOptionalText(owner?.Email),
                NormalizeOptionalText(tenant.Phone),
                businessType,
                PlatformTenantPresentationMapper.ComputeBusinessStatus(tenant),
                approvalStatus,
                PlatformTenantPresentationMapper.ToBusinessPlan(tenant.Plan),
                tenant.CreatedAt,
                lastLoginAt,
                tenant.TrialEndsAt,
                tenant.SubscriptionStatus.ToString(),
                BuildSubmittedDocuments(tenant, approvalStatus)));
        }

        var stats = await BuildTenantStatsAsync(cancellationToken);

        return new PlatformTenantsListResponseDto(items, total, normalized.Page, normalized.PageSize, stats);
    }

    public async Task<PlatformTenantDetailsDto?> GetTenantDetailsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _masterDb.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant == null)
        {
            return null;
        }

        var owner = await _appDb.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .OrderByDescending(u => tenant.OwnerUserId.HasValue && u.Id == tenant.OwnerUserId.Value)
            .ThenByDescending(u => u.Role == "Admin")
            .ThenBy(u => u.CreatedAt)
            .Select(u => new { u.FullName, u.Email })
            .FirstOrDefaultAsync(cancellationToken);

        var lastLoginAt = await _appDb.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .MaxAsync(u => (DateTime?)u.LastLoginAt, cancellationToken);

        int? remainingTrialDays = tenant.TrialEndsAt.HasValue
            ? Math.Max(0, (int)Math.Ceiling((tenant.TrialEndsAt.Value - DateTime.UtcNow).TotalDays))
            : null;

        return new PlatformTenantDetailsDto(
            tenant.Id,
            tenant.Name,
            NormalizeOptionalText(tenant.LegalBusinessName),
            tenant.Subdomain,
            null,
            NormalizeOptionalText(owner?.FullName),
            NormalizeOptionalText(owner?.Email),
            NormalizeOptionalText(tenant.Phone),
            PlatformTenantPresentationMapper.ComputeBusinessStatus(tenant),
            tenant.CreatedAt,
            lastLoginAt,
            new PlatformTenantTrialInfoDto(
                tenant.IsTrial,
                tenant.TrialStartsAt,
                tenant.TrialEndsAt,
                remainingTrialDays),
            new PlatformTenantSubscriptionInfoDto(
                PlatformTenantPresentationMapper.ToBusinessPlan(tenant.Plan),
                tenant.SubscriptionStatus.ToString(),
                tenant.TrialStartsAt,
                tenant.SubscriptionEndsAt,
                tenant.SubscriptionEndsAt,
                tenant.BillingCycle.ToString(),
                tenant.IsSuspended));
    }

    public async Task<IReadOnlyList<PlatformTenantUserDto>> GetTenantUsersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await EnsureTenantExistsAsync(tenantId, cancellationToken);

        var users = await _appDb.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new PlatformTenantUserDto(
                u.Id,
                string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName!,
                u.Email,
                u.Role,
                u.IsActive ? "Active" : "Inactive",
                u.LastLoginAt,
                u.CreatedAt))
            .ToListAsync(cancellationToken);

        return users;
    }

    public async Task<PlatformTenantServicesDto> GetTenantServicesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await EnsureTenantExistsAsync(tenantId, cancellationToken);

        var whatsApp = await _appDb.WhatsAppSettings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken);

        var hasTenantEmail = await _appDb.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == tenantId && !string.IsNullOrWhiteSpace(u.Email), cancellationToken);

        var stripeStatus = string.IsNullOrWhiteSpace(tenant.StripeCustomerId)
            ? "Disconnected"
            : (tenant.SubscriptionStatus == SubscriptionStatus.PendingApproval ? "Pending" : "Connected");

        var whatsAppStatus = whatsApp == null
            ? "Disconnected"
            : whatsApp.ConnectionStatus switch
            {
                WhatsAppConnectionStatus.Connected => "Connected",
                WhatsAppConnectionStatus.Connecting => "Pending",
                _ => "Disconnected"
            };

        var emailStatus = hasTenantEmail ? "Connected" : "Disconnected";
        var storageStatus = !string.IsNullOrWhiteSpace(tenant.LogoUrl) || !string.IsNullOrWhiteSpace(tenant.BusinessStampUrl)
            ? "Connected"
            : "Disconnected";

        return new PlatformTenantServicesDto(
            stripeStatus,
            whatsAppStatus,
            emailStatus,
            storageStatus);
    }

    public async Task<IReadOnlyList<PlatformTenantActivityDto>> GetTenantActivityAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await EnsureTenantExistsAsync(tenantId, cancellationToken);

        var items = await _appDb.AuditLogs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .Select(a => new PlatformTenantActivityDto(
                a.Id,
                string.IsNullOrWhiteSpace(a.ActionType) ? "Activity" : a.ActionType,
                BuildActivityDescription(a),
                a.CreatedAt,
                "AuditLog"))
            .ToListAsync(cancellationToken);

        return items;
    }

    public async Task ApproveTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tenant = await EnsureTenantExistsAsync(tenantId, cancellationToken);
        if (tenant.SubscriptionStatus != SubscriptionStatus.PendingApproval)
        {
            throw new PlatformTenantConflictException("Only pending businesses can be approved.");
        }

        await _tenantApprovalService.ApproveTenantAsync(tenantId, cancellationToken);
        await LogTenantLifecycleEventAsync(tenantId, "Business Approved", "Business approved by platform owner.", cancellationToken);
    }

    public async Task RejectTenantAsync(Guid tenantId, string? reason, CancellationToken cancellationToken)
    {
        var tenant = await EnsureTenantExistsAsync(tenantId, cancellationToken);

        if (tenant.SubscriptionStatus != SubscriptionStatus.PendingApproval)
        {
            throw new PlatformTenantConflictException("Only pending businesses can be rejected.");
        }

        tenant.SubscriptionStatus = SubscriptionStatus.Canceled;
        tenant.IsSuspended = true;
        tenant.IsTrial = false;
        tenant.TrialStartsAt = null;
        tenant.TrialEndsAt = null;

        await _masterDb.SaveChangesAsync(cancellationToken);
        await LogTenantLifecycleEventAsync(
            tenantId,
            "Business Rejected",
            string.IsNullOrWhiteSpace(reason) ? "Business rejected by platform owner." : $"Business rejected: {reason.Trim()}",
            cancellationToken);
    }

    public async Task SuspendTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await EnsureTenantExistsAsync(tenantId, cancellationToken);

        if (tenant.IsSuspended)
        {
            throw new PlatformTenantConflictException("Business is already suspended.");
        }

        tenant.IsSuspended = true;
        await _masterDb.SaveChangesAsync(cancellationToken);
    }

    public async Task ActivateTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await EnsureTenantExistsAsync(tenantId, cancellationToken);

        if (!tenant.IsSuspended)
        {
            throw new PlatformTenantConflictException("Business is already active.");
        }

        tenant.IsSuspended = false;
        await _masterDb.SaveChangesAsync(cancellationToken);
    }

    public async Task ExtendTrialAsync(Guid tenantId, int days, CancellationToken cancellationToken)
    {
        if (days <= 0 || days > 365)
        {
            throw new PlatformTenantValidationException("Days must be between 1 and 365.");
        }

        var tenant = await EnsureTenantExistsAsync(tenantId, cancellationToken);

        var canExtendTrial = tenant.IsTrial ||
                             tenant.Plan == PlanType.Trial ||
                             tenant.SubscriptionStatus == SubscriptionStatus.Trialing;

        if (!canExtendTrial)
        {
            throw new PlatformTenantConflictException("Trial can be extended only for trial businesses.");
        }

        var start = tenant.TrialEndsAt.HasValue && tenant.TrialEndsAt.Value > DateTime.UtcNow
            ? tenant.TrialEndsAt.Value
            : DateTime.UtcNow;

        tenant.IsTrial = true;
        tenant.TrialStartsAt ??= DateTime.UtcNow;
        tenant.TrialEndsAt = start.AddDays(days);

        if (tenant.SubscriptionStatus == SubscriptionStatus.PendingApproval)
        {
            tenant.SubscriptionStatus = SubscriptionStatus.Trialing;
        }

        await _masterDb.SaveChangesAsync(cancellationToken);
    }

    private static PlatformTenantsQuery NormalizeQuery(PlatformTenantsQuery query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 100);

        return query with
        {
            Page = page,
            PageSize = pageSize,
            SearchName = query.SearchName?.Trim(),
            SearchOwner = query.SearchOwner?.Trim(),
            RegistrationDate = query.RegistrationDate?.Trim(),
            BusinessType = query.BusinessType?.Trim(),
            Status = query.Status?.Trim(),
            Plan = query.Plan?.Trim(),
            Trial = query.Trial?.Trim(),
            Sort = string.IsNullOrWhiteSpace(query.Sort) ? "created_desc" : query.Sort.Trim()
        };
    }

    private static IQueryable<Tenant> ApplySort(IQueryable<Tenant> query, string? sort)
    {
        return sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(t => t.Name),
            "created_asc" => query.OrderBy(t => t.CreatedAt),
            _ => query.OrderByDescending(t => t.CreatedAt)
        };
    }

    private static bool TryParseBusinessPlan(string? plan, out PlanType value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(plan) || string.Equals(plan, "all", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = plan.Trim().ToLowerInvariant() switch
        {
            "starter" => PlanType.Basic,
            "growth" => PlanType.Pro,
            "enterprise" => PlanType.Enterprise,
            _ => default
        };

        return value is PlanType.Basic or PlanType.Pro or PlanType.Enterprise;
    }

    private static bool TryParseStatusFilter(string? status, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = status.Trim().ToLowerInvariant();
        return normalized is "pending" or "under_review" or "approved" or "rejected" or "trial" or "active" or "suspended" or "expired";
    }

    private static IQueryable<Tenant> ApplyStatusFilter(IQueryable<Tenant> query, string status)
    {
        var nowUtc = DateTime.UtcNow;

        return status switch
        {
            "pending" => query.Where(t => t.SubscriptionStatus == SubscriptionStatus.PendingApproval),
            "under_review" => query.Where(t => t.SubscriptionStatus == SubscriptionStatus.PendingApproval && !string.IsNullOrWhiteSpace(t.BusinessRegistrationNumber)),
            "approved" => query.Where(t => !t.IsSuspended && t.SubscriptionStatus != SubscriptionStatus.PendingApproval),
            "rejected" => query.Where(t => t.IsSuspended && t.SubscriptionStatus == SubscriptionStatus.Canceled),
            "trial" => query.Where(t => t.TrialEndsAt.HasValue && t.TrialEndsAt > nowUtc && !t.IsSuspended),
            "active" => query.Where(t => t.SubscriptionStatus == SubscriptionStatus.Active && !t.IsSuspended),
            "suspended" => query.Where(t => t.IsSuspended),
            "expired" => query.Where(t =>
                (t.SubscriptionStatus == SubscriptionStatus.Canceled ||
                 t.SubscriptionStatus == SubscriptionStatus.Unpaid ||
                 t.SubscriptionStatus == SubscriptionStatus.PastDue ||
                 t.SubscriptionStatus == SubscriptionStatus.GracePeriod) ||
                (t.TrialEndsAt.HasValue && t.TrialEndsAt <= nowUtc)),
            _ => query
        };
    }

    private static bool TryParseRegistrationDateFilter(string? registrationDate, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(registrationDate) || string.Equals(registrationDate, "all", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = registrationDate.Trim().ToLowerInvariant();
        return normalized is "today" or "last_7_days" or "last_30_days";
    }

    private static IQueryable<Tenant> ApplyRegistrationDateFilter(IQueryable<Tenant> query, string registrationDate)
    {
        var now = DateTime.UtcNow;
        var startOfToday = now.Date;

        return registrationDate switch
        {
            "today" => query.Where(t => t.CreatedAt >= startOfToday),
            "last_7_days" => query.Where(t => t.CreatedAt >= startOfToday.AddDays(-7)),
            "last_30_days" => query.Where(t => t.CreatedAt >= startOfToday.AddDays(-30)),
            _ => query
        };
    }

    private static bool TryParseBusinessTypeFilter(string? businessType, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(businessType) || string.Equals(businessType, "all", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = businessType.Trim().ToLowerInvariant();
        return normalized is "registered" or "unspecified";
    }

    private static IQueryable<Tenant> ApplyBusinessTypeFilter(IQueryable<Tenant> query, string businessType)
    {
        return businessType switch
        {
            "registered" => query.Where(t => !string.IsNullOrWhiteSpace(t.LegalBusinessName)),
            "unspecified" => query.Where(t => string.IsNullOrWhiteSpace(t.LegalBusinessName)),
            _ => query
        };
    }

    private static bool TryParseTrialFilter(string? trial, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(trial) || string.Equals(trial, "all", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = trial.Trim().ToLowerInvariant();
        return normalized is "on_trial" or "expired" or "not_on_trial";
    }

    private static IQueryable<Tenant> ApplyTrialFilter(IQueryable<Tenant> query, string trial)
    {
        var nowUtc = DateTime.UtcNow;

        return trial switch
        {
            "on_trial" => query.Where(t => t.TrialEndsAt.HasValue && t.TrialEndsAt > nowUtc),
            "expired" => query.Where(t => t.TrialEndsAt.HasValue && t.TrialEndsAt <= nowUtc),
            "not_on_trial" => query.Where(t => !t.TrialEndsAt.HasValue || t.TrialEndsAt <= nowUtc),
            _ => query
        };
    }

    private async Task<PlatformTenantsStatsDto> BuildTenantStatsAsync(CancellationToken cancellationToken)
    {
        var tenants = await _masterDb.Tenants
            .AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.CreatedAt,
                t.IsSuspended,
                t.SubscriptionStatus,
                t.IsTrial,
                t.TrialEndsAt
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var pending = tenants.Count(t => t.SubscriptionStatus == SubscriptionStatus.PendingApproval);
        var trial = tenants.Count(t => t.TrialEndsAt.HasValue && t.TrialEndsAt.Value > now && !t.IsSuspended);
        var active = tenants.Count(t => t.SubscriptionStatus == SubscriptionStatus.Active && !t.IsSuspended);
        var suspended = tenants.Count(t => t.IsSuspended);

        var startOfDay = now.Date;
        var endOfDay = startOfDay.AddDays(1);

        var approvedToday = await _appDb.AuditLogs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(a => a.ActionType == "Business Approved" && a.CreatedAt >= startOfDay && a.CreatedAt < endOfDay, cancellationToken);

        var rejectedToday = await _appDb.AuditLogs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(a => a.ActionType == "Business Rejected" && a.CreatedAt >= startOfDay && a.CreatedAt < endOfDay, cancellationToken);

        var firstApprovalsByTenant = await _appDb.AuditLogs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.ActionType == "Business Approved")
            .GroupBy(a => a.TenantId)
            .Select(g => new { TenantId = g.Key, ApprovedAt = g.Min(x => x.CreatedAt) })
            .ToListAsync(cancellationToken);

        var createdAtByTenant = tenants.ToDictionary(t => t.Id, t => t.CreatedAt);

        var approvalDurationsHours = firstApprovalsByTenant
            .Where(x => createdAtByTenant.ContainsKey(x.TenantId) && x.ApprovedAt >= createdAtByTenant[x.TenantId])
            .Select(x => (x.ApprovedAt - createdAtByTenant[x.TenantId]).TotalHours)
            .ToList();

        var averageApprovalTimeHours = approvalDurationsHours.Count == 0
            ? 0d
            : Math.Round(approvalDurationsHours.Average(), 2);

        return new PlatformTenantsStatsDto(
            tenants.Count,
            pending,
            trial,
            active,
            suspended,
            approvedToday,
            rejectedToday,
            averageApprovalTimeHours);
    }

    private async Task<Tenant> EnsureTenantExistsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _masterDb.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant == null)
        {
            throw new PlatformTenantNotFoundException(tenantId);
        }

        return tenant;
    }

    private static string BuildActivityDescription(AuditLog auditLog)
    {
        if (!string.IsNullOrWhiteSpace(auditLog.PerformedBy) && !string.IsNullOrWhiteSpace(auditLog.EntityName))
        {
            return $"{auditLog.PerformedBy} {auditLog.ActionType} {auditLog.EntityName}";
        }

        if (!string.IsNullOrWhiteSpace(auditLog.EntityName))
        {
            return $"{auditLog.ActionType} {auditLog.EntityName} ({auditLog.EntityId})";
        }

        return "Activity event";
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ResolveBusinessType(Tenant tenant)
    {
        return string.IsNullOrWhiteSpace(tenant.LegalBusinessName) ? "Unspecified" : "Registered";
    }

    private static string ResolveApprovalStatus(Tenant tenant, string? latestAuditStatus)
    {
        if (latestAuditStatus == "Business Rejected")
        {
            return "Rejected";
        }

        if (latestAuditStatus == "Business Approved")
        {
            return "Approved";
        }

        if (tenant.SubscriptionStatus == SubscriptionStatus.PendingApproval)
        {
            return string.IsNullOrWhiteSpace(tenant.BusinessRegistrationNumber) ? "Pending" : "Under Review";
        }

        if (tenant.SubscriptionStatus == SubscriptionStatus.Canceled && tenant.IsSuspended)
        {
            return "Rejected";
        }

        return "Approved";
    }

    private static PlatformSubmittedDocumentsDto BuildSubmittedDocuments(Tenant tenant, string approvalStatus)
    {
        var businessLicenseUploaded = !string.IsNullOrWhiteSpace(tenant.BusinessRegistrationNumber);

        var businessLicense = businessLicenseUploaded ? "Uploaded" : "Missing";
        var taxRegistration = businessLicenseUploaded ? "Uploaded" : "Missing";
        var identityDocument = "Missing";
        var proofOfAddress = "Missing";

        if (approvalStatus == "Approved")
        {
            businessLicense = businessLicenseUploaded ? "Verified" : "Missing";
            taxRegistration = businessLicenseUploaded ? "Verified" : "Missing";
        }

        if (approvalStatus == "Rejected")
        {
            businessLicense = businessLicenseUploaded ? "Rejected" : "Missing";
            taxRegistration = businessLicenseUploaded ? "Rejected" : "Missing";
        }

        return new PlatformSubmittedDocumentsDto(
            businessLicense,
            identityDocument,
            proofOfAddress,
            taxRegistration);
    }

    private async Task LogTenantLifecycleEventAsync(
        Guid tenantId,
        string actionType,
        string description,
        CancellationToken cancellationToken)
    {
        _appDb.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = null,
            EntityName = "Tenant",
            EntityId = tenantId.ToString(),
            ActionType = actionType,
            OldValues = null,
            NewValues = JsonSerializer.Serialize(new { action = actionType, description }),
            PerformedBy = "PlatformOwner",
            CreatedAt = DateTime.UtcNow
        });

        await _appDb.SaveChangesAsync(cancellationToken);
    }
}
