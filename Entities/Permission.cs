namespace Clienta.Api.Entities;

public class Permission : ITenantEntity
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public virtual Tenant Tenant { get; set; } = default!;
}
