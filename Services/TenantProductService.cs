using Clienta.Api.Data;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface ITenantProductService
{
    ResolvedTenantProduct Resolve(TenantProductType? productType);
    Task<ResolvedTenantProduct> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
    string ToApiValue(ResolvedTenantProduct product);
}

// Single authoritative, read-only product resolver. NULL means Legacy CLIENTA and is never written or coerced to Clinic.
public class TenantProductService : ITenantProductService
{
    private readonly AppDbContext _context;

    public TenantProductService(AppDbContext context)
    {
        _context = context;
    }

    public ResolvedTenantProduct Resolve(TenantProductType? productType) => productType switch
    {
        TenantProductType.Clinic => ResolvedTenantProduct.Clinic,
        TenantProductType.Business => ResolvedTenantProduct.Business,
        _ => ResolvedTenantProduct.Legacy
    };

    public static bool TryParseRegistrationProduct(string? value, out TenantProductType productType)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "clinic":
                productType = TenantProductType.Clinic;
                return true;
            case "business":
                productType = TenantProductType.Business;
                return true;
            default:
                productType = default;
                return false;
        }
    }

    // Unknown tenant or empty tenant id resolves to Legacy (existing behaviour).
    public async Task<ResolvedTenantProduct> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            return ResolvedTenantProduct.Legacy;

        var productType = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.ProductType)
            .FirstOrDefaultAsync(cancellationToken);

        return Resolve(productType);
    }

    public string ToApiValue(ResolvedTenantProduct product) => product switch
    {
        ResolvedTenantProduct.Clinic => "clinic",
        ResolvedTenantProduct.Business => "business",
        _ => "legacy"
    };
}
