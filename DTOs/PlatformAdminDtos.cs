using System.ComponentModel.DataAnnotations;
using Clienta.Api.Models;

namespace Clienta.Api.DTOs;

public record PlatformUsersQuery(
    [param: Range(1, int.MaxValue)]
    int Page = 1,
    [param: Range(1, 100)]
    int PageSize = 10,
    [param: StringLength(200)]
    string? Search = null,
    string? Role = null,
    string? Status = null,
    string? Sort = null);

public record PlatformUserListItemDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool PasswordResetRequired);

public record PlatformUsersStatsDto(
    int TotalUsers,
    int Active,
    int Disabled,
    int SuperAdmins,
    int SupportStaff);

public record PlatformUsersListResponseDto(
    IReadOnlyList<PlatformUserListItemDto> Items,
    int Total,
    int Page,
    int PageSize,
    PlatformUsersStatsDto Stats);

public record PlatformUserCreateRequest(
    [param: Required]
    [param: StringLength(120, MinimumLength = 2)]
    string FullName,
    [param: Required]
    [param: EmailAddress]
    string Email,
    [param: Required]
    [param: StringLength(128, MinimumLength = 8)]
    string Password,
    [param: Required]
    string Role,
    bool IsActive = true,
    bool ForcePasswordReset = false);

public record PlatformUserUpdateRequest(
    [param: StringLength(120, MinimumLength = 2)]
    string? FullName,
    [param: EmailAddress]
    string? Email,
    string? Role,
    bool? IsActive);

public record PlatformUserPasswordResetRequest(
    [param: Required]
    [param: StringLength(128, MinimumLength = 8)]
    string NewPassword);

public record PlatformUserForcePasswordResetRequest();

public record PlatformUserRoleRequest(
    [param: Required]
    string Role);

public record PlatformSettingsDto(
    Guid Id,
    string SupportEmail,
    string? SupportPhone,
    string? WebsiteUrl,
    int DefaultTrialDays,
    int TrialReminderDays,
    bool AllowRegistrations,
    bool RequireManualApproval,
    bool EnableBilling,
    bool EnableHelpCenter,
    bool WhatsAppEnabled,
    decimal ProMonthlyPrice,
    decimal ProAnnualPrice,
    string ProDescription,
    bool ProEnabled,
    int ProDisplayOrder,
    DateTime UpdatedAt);

public record UpdatePlatformSettingsRequest(
    string SupportEmail,
    string? SupportPhone,
    string? WebsiteUrl,
    int DefaultTrialDays,
    int TrialReminderDays,
    bool AllowRegistrations,
    bool RequireManualApproval,
    bool EnableBilling,
    bool EnableHelpCenter,
    bool WhatsAppEnabled,
    decimal ProMonthlyPrice,
    decimal ProAnnualPrice,
    string ProDescription,
    bool ProEnabled,
    int ProDisplayOrder);
