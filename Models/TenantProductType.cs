namespace Clienta.Api.Models;

// Persisted product discriminator. Legacy CLIENTA is deliberately NOT a value: it is Tenant.ProductType == null.
// Values start at 1 so a default(0) can never be read as a real product.
public enum TenantProductType
{
    Clinic = 1,
    Business = 2
}

// Read-model only; never persisted.
public enum ResolvedTenantProduct
{
    Legacy = 0,
    Clinic = 1,
    Business = 2
}
