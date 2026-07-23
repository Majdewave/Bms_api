using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services.Platform;

public class PlatformDashboardService : IPlatformDashboardService
{
    private sealed record TenantSummaryLite(
        Guid Id,
        string Name,
        DateTime CreatedAt,
        Guid? OwnerUserId,
        SubscriptionStatus SubscriptionStatus,
        PlanType Plan,
        bool IsTrial,
        DateTime? TrialStartsAt,
        DateTime? TrialEndsAt,
        bool IsSuspended);

    private sealed record TenantUserLite(
        Guid TenantId,
        Guid Id,
        string? FullName,
        string Email,
        string Role,
        DateTime CreatedAt);

    private readonly MasterDbContext _masterDb;
    private readonly AppDbContext _appDb;
    private readonly IConfiguration _configuration;

    public PlatformDashboardService(
        MasterDbContext masterDb,
        AppDbContext appDb,
        IConfiguration configuration)
    {
        _masterDb = masterDb;
        _appDb = appDb;
        _configuration = configuration;
    }

    public async Task<PlatformDashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var tenantSummary = await _masterDb.Tenants
            .AsNoTracking()
            .Select(t => new TenantSummaryLite(
                t.Id,
                t.Name,
                t.CreatedAt,
                t.OwnerUserId,
                t.SubscriptionStatus,
                t.Plan,
                t.IsTrial,
                t.TrialStartsAt,
                t.TrialEndsAt,
                t.IsSuspended))
            .ToListAsync(cancellationToken);

        var statistics = BuildStatistics(tenantSummary, now);

        var upcomingTrialTenants = tenantSummary
            .Where(t => t.TrialEndsAt.HasValue && t.TrialEndsAt.Value > now && t.TrialEndsAt.Value <= now.AddDays(14))
            .OrderBy(t => t.TrialEndsAt)
            .Take(10)
            .ToList();

        var recentRegistrationTenants = tenantSummary
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .ToList();

        var userActivityEvents = await _appDb.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .OrderByDescending(u => u.CreatedAt)
            .Take(20)
            .Select(u => new PlatformDashboardActivityItemDto(
                Guid.NewGuid(),
                "User Created",
                string.IsNullOrWhiteSpace(u.FullName)
                    ? $"User {u.Email} created"
                    : $"User {u.FullName} created",
                u.CreatedAt,
                "Users"))
            .ToListAsync(cancellationToken);

        var auditActivityEvents = await _appDb.AuditLogs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .OrderByDescending(a => a.CreatedAt)
            .Take(30)
            .Select(a => new PlatformDashboardActivityItemDto(
                a.Id,
                string.IsNullOrWhiteSpace(a.ActionType) ? "Activity" : a.ActionType,
                BuildAuditDescription(a),
                a.CreatedAt,
                "AuditLog"))
            .ToListAsync(cancellationToken);

        var tenantIdsForOwners = upcomingTrialTenants
            .Select(t => t.Id)
            .Concat(recentRegistrationTenants.Select(t => t.Id))
            .Distinct()
            .ToList();

        var ownersByTenantId = await LoadOwnersByTenantAsync(tenantIdsForOwners, tenantSummary, cancellationToken);

        var upcomingTrials = upcomingTrialTenants
            .Select(t => new PlatformTrialExpiringItemDto(
                t.Id,
                t.Name,
                ownersByTenantId.TryGetValue(t.Id, out var ownerName) ? ownerName : null,
                t.TrialEndsAt!.Value,
                Math.Max(0, (int)Math.Ceiling((t.TrialEndsAt!.Value - now).TotalDays))))
            .ToList();

        var recentRegistrations = recentRegistrationTenants
            .Select(t => new PlatformRecentRegistrationItemDto(
                t.Id,
                t.Name,
                ownersByTenantId.TryGetValue(t.Id, out var ownerName) ? ownerName : null,
                t.CreatedAt,
                PlatformTenantPresentationMapper.ComputeBusinessStatus(
                    t.SubscriptionStatus,
                    t.IsTrial,
                    t.TrialEndsAt,
                    t.IsSuspended,
                    now)))
            .ToList();

        var registrationEvents = recentRegistrationTenants
            .Take(12)
            .Select(t => new PlatformDashboardActivityItemDto(
                Guid.NewGuid(),
                "Business Registered",
                $"{t.Name} registered on the platform",
                t.CreatedAt,
                "Tenants"));

        var trialExtendedEvents = tenantSummary
            .Where(t => t.TrialStartsAt.HasValue && t.TrialEndsAt.HasValue && t.TrialEndsAt.Value > t.TrialStartsAt.Value.AddDays(7))
            .OrderByDescending(t => t.TrialEndsAt)
            .Take(8)
            .Select(t => new PlatformDashboardActivityItemDto(
                Guid.NewGuid(),
                "Trial Extended",
                $"{t.Name} trial now ends on {t.TrialEndsAt:yyyy-MM-dd}",
                t.TrialEndsAt!.Value,
                "Trials"));

        var recentActivity = auditActivityEvents
            .Concat(registrationEvents)
            .Concat(trialExtendedEvents)
            .Concat(userActivityEvents)
            .OrderByDescending(a => a.Timestamp)
            .Take(20)
            .ToList();

        var health = await BuildHealthAsync(cancellationToken);

        return new PlatformDashboardDto(
            statistics,
            upcomingTrials,
            recentRegistrations,
            recentActivity,
            health);
    }

    private static PlatformTenantsStatsDto BuildStatistics(IEnumerable<TenantSummaryLite> tenants, DateTime now)
    {
        var total = 0;
        var pending = 0;
        var trial = 0;
        var active = 0;
        var suspended = 0;

        foreach (var tenant in tenants)
        {
            total++;

            var status = PlatformTenantPresentationMapper.ComputeBusinessStatus(
                tenant.SubscriptionStatus,
                tenant.IsTrial,
                tenant.TrialEndsAt,
                tenant.IsSuspended,
                now);

            switch (status)
            {
                case "Pending":
                    pending++;
                    break;
                case "Trial":
                    trial++;
                    break;
                case "Active":
                    active++;
                    break;
                case "Suspended":
                    suspended++;
                    break;
            }
        }

        return new PlatformTenantsStatsDto(total, pending, trial, active, suspended, 0, 0, 0d);
    }

    private async Task<Dictionary<Guid, string?>> LoadOwnersByTenantAsync(
        IReadOnlyCollection<Guid> tenantIds,
        IReadOnlyCollection<TenantSummaryLite> tenants,
        CancellationToken cancellationToken)
    {
        if (tenantIds.Count == 0)
        {
            return new Dictionary<Guid, string?>();
        }

        var ownerIdsByTenant = tenants
            .Where(t => tenantIds.Contains(t.Id))
            .Where(t => t.OwnerUserId != null)
            .ToDictionary(t => t.Id, t => t.OwnerUserId);

        var users = await _appDb.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => tenantIds.Contains(u.TenantId))
            .Select(u => new TenantUserLite(u.TenantId, u.Id, u.FullName, u.Email, u.Role, u.CreatedAt))
            .ToListAsync(cancellationToken);

        var byTenant = users
            .GroupBy(u => u.TenantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<Guid, string?>();
        foreach (var tenantId in tenantIds)
        {
            byTenant.TryGetValue(tenantId, out var tenantUsers);
            if (tenantUsers == null || tenantUsers.Count == 0)
            {
                result[tenantId] = null;
                continue;
            }

            var ownerUserId = ownerIdsByTenant.TryGetValue(tenantId, out var knownOwnerUserId)
                ? knownOwnerUserId
                : null;

            var owner = tenantUsers
                .OrderByDescending(u => ownerUserId.HasValue && u.Id == ownerUserId.Value)
                .ThenByDescending(u => string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                .ThenBy(u => u.CreatedAt)
                .FirstOrDefault();

            result[tenantId] = string.IsNullOrWhiteSpace(owner?.FullName) ? owner?.Email : owner.FullName;
        }

        return result;
    }

    private async Task<PlatformHealthDto> BuildHealthAsync(CancellationToken cancellationToken)
    {
        var api = new PlatformHealthItemDto("Healthy", "API is operational.");

        PlatformHealthItemDto database;
        try
        {
            var canConnect = await _masterDb.Database.CanConnectAsync(cancellationToken);
            database = canConnect
                ? new PlatformHealthItemDto("Healthy", "Database connection is available.")
                : new PlatformHealthItemDto("Offline", "Database connection is unavailable.");
        }
        catch
        {
            database = new PlatformHealthItemDto("Offline", "Database connection check failed.");
        }

        var storage = BuildConfigHealth("AWS:BucketName", "Storage bucket configuration detected.", "Storage bucket is not configured.");
        var email = BuildConfigHealth("SendGrid:ApiKey", "Email provider configuration detected.", "Email provider API key is missing.");
        var whatsApp = BuildConfigHealth("WhatsApp:MetaAccessToken", "WhatsApp configuration detected.", "WhatsApp access token is missing.");
        var stripe = BuildConfigHealth("Stripe:SecretKey", "Stripe configuration detected.", "Stripe secret key is missing.");

        return new PlatformHealthDto(api, database, storage, email, whatsApp, stripe);
    }

    private PlatformHealthItemDto BuildConfigHealth(string key, string healthyMessage, string warningMessage)
    {
        var value = _configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return new PlatformHealthItemDto("Healthy", healthyMessage);
        }

        return new PlatformHealthItemDto("Warning", warningMessage);
    }

    private static string BuildAuditDescription(AuditLog auditLog)
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
}
