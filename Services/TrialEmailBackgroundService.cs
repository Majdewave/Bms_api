using Clienta.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public class TrialEmailBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrialEmailBackgroundService> _logger;

    public TrialEmailBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<TrialEmailBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnce();
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunOnce()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var today = DateTime.UtcNow.Date;

        var tenants = await db.Tenants
            .Include(t => t.OwnerUser)
            .ToListAsync();

        foreach (var tenant in tenants)
        {
            if (tenant.OwnerUser == null || tenant.TrialEndsAt == null) continue;

            var daysLeft = ((DateTime)tenant.TrialEndsAt).Date.Subtract(today).Days;

            // Reminder - יום לפני
            if (daysLeft == 1)
            {
                await emailService.SendTrialReminderAsync(
                    tenant.OwnerUser.Email,
                    tenant.Name,
                    tenant.Subdomain,
                    daysLeft,
                    tenant.Id
                );
            }

            // Expired - אחרי סיום
            if (daysLeft < 0 && !tenant.IsTrialExpiredEmailSent)
            {
                await emailService.SendTrialExpiredAsync(
                    tenant.OwnerUser.Email,
                    tenant.Name,
                    tenant.Subdomain,
                    tenant.Id
                );

                tenant.IsTrialExpiredEmailSent = true;
            }
        }

        await db.SaveChangesAsync();
    }
}
