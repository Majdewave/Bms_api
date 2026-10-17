using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Models;

namespace Clienta.Api.Services;

public interface IPmAccessService
{
    Task<bool> IsPmEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

// PM access is a released Business capability for Trial and Basic plans. Clinic and
// Legacy tenants retain the original explicit TenantFeatures.PmEnabled behavior.
public class PmAccessService : IPmAccessService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public PmAccessService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<bool> IsPmEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            return false;

        if (_tenantContext.TenantId != Guid.Empty && _tenantContext.TenantId != tenantId)
            return false;

        var tenantPlan = await _context.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => new { tenant.ProductType, tenant.Plan })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenantPlan is not null &&
            BusinessPlanEntitlement.HasCurrentBusinessCapabilities(tenantPlan.ProductType, tenantPlan.Plan))
        {
            return true;
        }

        var pmEnabled = await _context.TenantFeatures
            .AsNoTracking()
            .Where(features => features.TenantId == tenantId)
            .Select(features => (bool?)features.PmEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        return pmEnabled ?? false;
    }
}
