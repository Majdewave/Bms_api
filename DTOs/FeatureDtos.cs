namespace Clienta.Api.DTOs;

public record UpdateTenantFeaturesRequest(
    bool ReportsEnabled,
    bool InvoicesEnabled,
    bool PrescriptionsEnabled,
    bool DrugsEnabled,
    bool BeforeAfterPhotosEnabled,
    bool VisitSummariesEnabled
);
