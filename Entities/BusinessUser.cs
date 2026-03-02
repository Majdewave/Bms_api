namespace Clienta.Api.Entities;

public class BusinessUser : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public virtual Tenant Tenant { get; set; } = default!;

    public Guid UserId { get; set; }
    public virtual User User { get; set; } = null!;
}
