using Clienta.Api.DTOs;

namespace Clienta.Api.Services.Platform;

public interface IPlatformTenantManagementService
{
    Task<PlatformTenantsListResponseDto> GetTenantsAsync(PlatformTenantsQuery query, CancellationToken cancellationToken);
    Task<PlatformTenantDetailsDto?> GetTenantDetailsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformTenantUserDto>> GetTenantUsersAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<PlatformTenantServicesDto> GetTenantServicesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformTenantActivityDto>> GetTenantActivityAsync(Guid tenantId, CancellationToken cancellationToken);
    Task ApproveTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task RejectTenantAsync(Guid tenantId, string? reason, CancellationToken cancellationToken);
    Task SuspendTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task ActivateTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task ExtendTrialAsync(Guid tenantId, int days, CancellationToken cancellationToken);
}
