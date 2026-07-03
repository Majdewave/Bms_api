namespace Clienta.Api.DTOs;

public record TenantResponse(
    Guid Id,
    string Name,
    string? LegalBusinessName,
    string? BusinessRegistrationNumber,
    string Subdomain,
    string? LogoUrl,
    string? BusinessStampUrl,
    string Plan,
    string SubscriptionStatus,
    DateTime CreatedAt,
    int AutoDeleteNotDocumentedAfterDays,
    bool EnableAutoDeleteNotDocumented,
    decimal DefaultVatRate,
    decimal DefaultWithholdingTaxRate,
    string DefaultPaymentMethod,
    int? DefaultInstallments,
    string DefaultInvoiceStatus,
    string Currency,
    string InvoicePrefix,
    int NextInvoiceNumber
);

public record AutoDeleteSettingsRequest(
    int Days,
    bool Enabled
);
