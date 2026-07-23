using Clienta.Api.DTOs;

namespace Clienta.Api.Services.Platform;

public interface IPlatformDashboardService
{
    Task<PlatformDashboardDto> GetDashboardAsync(CancellationToken cancellationToken);
}
