namespace Clienta.Api.Services;

public interface ITenantSeedService
{
    Task SeedAdvancedAsync(Guid tenantId, Guid adminUserId);
}
