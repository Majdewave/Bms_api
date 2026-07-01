namespace Clienta.Api.DTOs;

public record TenantResponse(
    Guid Id,
    string Name,
    string Subdomain,
    string? LogoUrl,
    string? BusinessStampUrl,
    string Plan,
    string SubscriptionStatus,
    DateTime CreatedAt,
    int AutoDeleteNotDocumentedAfterDays,
    bool EnableAutoDeleteNotDocumented,
    decimal DefaultVatRate,
    string Currency,
    string InvoicePrefix,
    int NextInvoiceNumber
);

public record AutoDeleteSettingsRequest(
    int Days,
    bool Enabled
);
