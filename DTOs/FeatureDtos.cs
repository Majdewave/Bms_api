namespace Clienta.Api.DTOs;

public record UpdateTenantFeaturesRequest(
    bool ReportsEnabled,
    bool InvoicesEnabled,
    bool QuotesEnabled,
    bool PrescriptionsEnabled,
    bool DrugsEnabled,
    bool BeforeAfterPhotosEnabled,
    bool VisitSummariesEnabled,
    bool TeamChatEnabled,
    bool QueueDisplayEnabled,
    bool WhatsAppEnabled,
    bool NotDocumentedEnabled,
    bool MedicalImagingEnabled
);

public record EffectiveDepartmentFeaturesResponse(
    bool QuotesEnabled,
    bool PrescriptionsEnabled,
    bool DrugsEnabled,
    bool ConsentFormsEnabled,
    bool VisitSummariesEnabled,
    bool BeforeAfterPhotosEnabled,
    bool TeamChatEnabled,
    bool WhatsAppEnabled,
    bool NotDocumentedEnabled,
    bool MedicalImagingEnabled
);