using Microsoft.AspNetCore.SignalR;

namespace Clienta.Api.Hubs;

public class AppointmentsHub : Hub
{
    public async Task JoinTenant(string tenantId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, tenantId);
    }
}
