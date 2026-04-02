using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class CleanupService : BackgroundService
{
private readonly IServiceProvider _serviceProvider;

public CleanupService(IServiceProvider serviceProvider)
{
    _serviceProvider = serviceProvider;
}

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        var now = DateTime.UtcNow;
        var nextRun = DateTime.UtcNow.Date.AddDays(1);
        var delay = nextRun - now;

        await Task.Delay(delay, stoppingToken);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenants = await context.Tenants.ToListAsync();

        foreach (var tenant in tenants)
        {
            if (!tenant.EnableAutoDeleteNotDocumented)
                continue;

            var days = tenant.AutoDeleteNotDocumentedAfterDays;
            List<Client> clientsToDelete;

            if (days == 0)
            {
                clientsToDelete = context.Clients
                    .Where(c =>
                        c.TenantId == tenant.Id &&
                        !c.IsDocumented)
                    .ToList();
            }
            else
            {
                var cutoff = DateTime.UtcNow.AddDays(-days);

                clientsToDelete = context.Clients
                    .Where(c =>
                        c.TenantId == tenant.Id &&
                        !c.IsDocumented &&
                        c.CreatedAt <= cutoff)
                    .ToList();
            }

            context.Clients.RemoveRange(clientsToDelete);
        }

        await context.SaveChangesAsync();
    }
}
}
