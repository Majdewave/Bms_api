namespace Clienta.Api.DTOs;

public record PlatformDashboardDto(
    PlatformTenantsStatsDto Statistics,
    IReadOnlyList<PlatformTrialExpiringItemDto> UpcomingTrials,
    IReadOnlyList<PlatformRecentRegistrationItemDto> RecentRegistrations,
    IReadOnlyList<PlatformDashboardActivityItemDto> RecentActivity,
    PlatformHealthDto PlatformHealth);

public record PlatformTrialExpiringItemDto(
    Guid TenantId,
    string BusinessName,
    string? OwnerName,
    DateTime TrialEndsAt,
    int DaysRemaining);

public record PlatformRecentRegistrationItemDto(
    Guid TenantId,
    string BusinessName,
    string? OwnerName,
    DateTime CreatedAt,
    string Status);

public record PlatformDashboardActivityItemDto(
    Guid Id,
    string Title,
    string Description,
    DateTime Timestamp,
    string Source);

public record PlatformHealthDto(
    PlatformHealthItemDto Api,
    PlatformHealthItemDto Database,
    PlatformHealthItemDto Storage,
    PlatformHealthItemDto Email,
    PlatformHealthItemDto WhatsApp,
    PlatformHealthItemDto Stripe);

public record PlatformHealthItemDto(
    string Status,
    string Message);
