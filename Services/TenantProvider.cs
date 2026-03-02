namespace Clienta.Api.Services;

/// <summary>
/// Provider for setting tenant context in the current request.
/// Used by TenantMiddleware to establish tenant isolation.
/// </summary>
public interface ITenantProvider
{
    void SetTenantId(Guid tenantId);
}

/// <summary>
/// Implementation that wraps ITenantContext.
/// </summary>
public class TenantProvider : ITenantProvider
{
    private readonly ITenantContext _tenantContext;

    public TenantProvider(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public void SetTenantId(Guid tenantId)
    {
        _tenantContext.SetTenant(tenantId);
    }
}
