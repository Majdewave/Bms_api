namespace Clienta.Api.Services;

public interface ITenantContext
{
    Guid TenantId { get; }
    Guid? UserId { get; }
    void SetTenant(Guid tenantId);
    void SetUserId(Guid userId);
}

public class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        TenantId = tenantId;
    }

    public void SetUserId(Guid userId)
    {
        UserId = userId;
    }
}
