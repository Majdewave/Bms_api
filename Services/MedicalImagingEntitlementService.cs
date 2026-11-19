using Clienta.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IMedicalImagingEntitlementService
{
    Task<bool> IsEntitledAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class MedicalImagingEntitlementService : IMedicalImagingEntitlementService
{
    private readonly AppDbContext _db;

    public MedicalImagingEntitlementService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsEntitledAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            return false;

        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => new
            {
                item.Plan,
                item.SubscriptionStatus,
                item.TrialEndsAt,
                item.SubscriptionEndsAt,
                item.IsSuspended
            })
            .FirstOrDefaultAsync(cancellationToken);

        return tenant != null && BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            tenant.Plan,
            tenant.SubscriptionStatus,
            tenant.TrialEndsAt,
            tenant.SubscriptionEndsAt,
            tenant.IsSuspended,
            DateTime.UtcNow);
    }
}
