namespace Clienta.Api.Services;

using Clienta.Api.Data;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

public interface IPaymentConfirmationAccessService
{
    Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class PaymentConfirmationAccessService : IPaymentConfirmationAccessService
{
    private readonly AppDbContext _db;

    public PaymentConfirmationAccessService(AppDbContext db) => _db = db;

    // DevelopmentEnabled was a temporary development-only rollout gate. Production
    // customer payment confirmations are enabled only for explicit Business tenants;
    // invoice permissions, tenant-scoped records and opaque tokens remain the security boundary.
    public Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        _db.Tenants.AsNoTracking().AnyAsync(
            tenant => tenant.Id == tenantId && tenant.ProductType == TenantProductType.Business,
            cancellationToken);
}