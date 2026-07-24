using Clienta.Api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Hubs;

public class QueueDisplayHub : Hub
{
    private readonly AppDbContext _db;

    public QueueDisplayHub(AppDbContext db)
    {
        _db = db;
    }

    public override async Task OnConnectedAsync()
    {
        var token = Context.GetHttpContext()?.Request.Query["displayToken"].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            Context.Abort();
            return;
        }

        var settings = await _db.QueueDisplaySettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PublicToken == token);

        if (settings == null)
        {
            Context.Abort();
            return;
        }

        var enabled = await _db.TenantFeatures
            .AsNoTracking()
            .Where(f => f.TenantId == settings.TenantId)
            .Select(f => f.QueueDisplayEnabled)
            .FirstOrDefaultAsync();

        if (!enabled)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(settings.TenantId));

        await base.OnConnectedAsync();
    }

    public static string GetGroupName(Guid tenantId) => $"queue-display:{tenantId}";
}
