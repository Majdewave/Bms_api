namespace Clienta.Api.Entities;

public class TenantFeatures : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public bool ReportsEnabled { get; set; } = false;
    public bool InvoicesEnabled { get; set; } = false;
    public bool PrescriptionsEnabled { get; set; } = false;
    public bool DrugsEnabled { get; set; } = false;
    public bool BeforeAfterPhotosEnabled { get; set; } = false;
    public bool VisitSummariesEnabled { get; set; } = false; 

    public virtual Tenant Tenant { get; set; } = default!;
}
