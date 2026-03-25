namespace Clienta.Api.Entities;

public class TenantFeatures : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public bool ReportsEnabled { get; set; } = true;
    public bool InvoicesEnabled { get; set; } = true;
    public bool PrescriptionsEnabled { get; set; } = false;

    public virtual Tenant Tenant { get; set; } = default!;
}
