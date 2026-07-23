using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.DTOs;

public record ApiErrorResponse(string Code, string Message);

public record PlatformTenantsQuery(
    [param: Range(1, int.MaxValue)]
    int Page = 1,
    [param: Range(1, 100)]
    int PageSize = 10,
    [param: StringLength(200)]
    string? SearchName = null,
    [param: StringLength(200)]
    string? SearchOwner = null,
    string? RegistrationDate = null,
    string? BusinessType = null,
    string? Status = null,
    string? Plan = null,
    string? Trial = null,
    string? Sort = null);

public record PlatformTenantsStatsDto(
    int Total,
    int Pending,
    int Trial,
    int Active,
    int Suspended,
    int ApprovedToday,
    int RejectedToday,
    double AverageApprovalTimeHours);

public record PlatformSubmittedDocumentsDto(
    string BusinessLicense,
    string IdentityDocument,
    string ProofOfAddress,
    string TaxRegistration);

public record PlatformTenantListItemDto(
    Guid TenantId,
    string BusinessName,
    string? OwnerName,
    string? OwnerEmail,
    string? OwnerPhone,
    string? BusinessType,
    string Status,
    string ApprovalStatus,
    string Plan,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    DateTime? TrialEndsAt,
    string SubscriptionStatus,
    PlatformSubmittedDocumentsDto SubmittedDocuments);

public record PlatformTenantsListResponseDto(
    IReadOnlyList<PlatformTenantListItemDto> Items,
    int Total,
    int Page,
    int PageSize,
    PlatformTenantsStatsDto Stats);

public record PlatformTenantTrialInfoDto(
    bool IsTrial,
    DateTime? TrialStartsAt,
    DateTime? TrialEndsAt,
    int? RemainingTrialDays);

public record PlatformTenantSubscriptionInfoDto(
    string Plan,
    string SubscriptionStatus,
    DateTime? SubscriptionStart,
    DateTime? SubscriptionEnd,
    DateTime? RenewalDate,
    string BillingCycle,
    bool IsSuspended);

public record PlatformTenantDetailsDto(
    Guid TenantId,
    string BusinessName,
    string? LegalName,
    string Subdomain,
    string? BusinessType,
    string? OwnerName,
    string? OwnerEmail,
    string? OwnerPhone,
    string Status,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    PlatformTenantTrialInfoDto Trial,
    PlatformTenantSubscriptionInfoDto Subscription);

public record PlatformTenantUserDto(
    Guid Id,
    string Name,
    string Email,
    string Role,
    string Status,
    DateTime? LastLoginAt,
    DateTime CreatedAt);

public record PlatformTenantServicesDto(
    string Stripe,
    string WhatsApp,
    string Email,
    string Storage);

public record PlatformTenantActivityDto(
    Guid Id,
    string Title,
    string Description,
    DateTime Timestamp,
    string Source);

public record ExtendTrialRequest(int Days);

public record RejectTenantRequest(string? Reason);
