namespace Clienta.Api.DTOs;

public record TenantResponse(
    Guid Id,
    string Name,
    string Subdomain,
    string? LogoUrl,
    string Plan,
    string SubscriptionStatus,
    DateTime CreatedAt,
    int AutoDeleteNotDocumentedAfterDays,
    bool EnableAutoDeleteNotDocumented
);

public record AutoDeleteSettingsRequest(
    int Days,
    bool Enabled
);
