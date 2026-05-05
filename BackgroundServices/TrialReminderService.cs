using Clienta.Api.Data;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.BackgroundServices;

public class TrialReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrialReminderService> _logger;

    public TrialReminderService(
        IServiceScopeFactory scopeFactory,
        ILogger<TrialReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Trial Reminder Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckTrialTenantsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Trial Reminder Service");
            }

            // Run every 24 hours
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task CheckTrialTenantsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var tenants = await db.Tenants
            .Where(t => t.Plan == PlanType.Trial 
                     && t.SubscriptionStatus == SubscriptionStatus.Trialing 
                     && !t.IsSuspended)
            .ToListAsync();

        _logger.LogInformation("Checking {Count} trial tenants", tenants.Count);

        foreach (var tenant in tenants)
        {
            if (!tenant.TrialEndsAt.HasValue) continue;
            
            var daysLeft = (tenant.TrialEndsAt.Value - DateTime.UtcNow).Days;

            try
            {
                // Get admin email for this tenant
                var adminEmail = await db.Users
                    .Where(u => u.TenantId == tenant.Id && u.Role == "Admin")
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrEmpty(adminEmail))
                {
                    _logger.LogWarning("No admin email found for tenant {TenantId}", tenant.Id);
                    continue;
                }

                // 7 days reminder
                if (daysLeft == 7)
                {
                    await emailService.SendTrialReminderAsync(
                        adminEmail,
                        tenant.Name,
                        tenant.Subdomain,
                        7, 
                        tenant.Id
                        );
                    _logger.LogInformation("Sent 7-day reminder to {Email}", adminEmail);
                }

                // 2 days reminder
                if (daysLeft == 2)
                {
                    await emailService.SendTrialReminderAsync(
                        adminEmail,
                        tenant.Name,
                        tenant.Subdomain,
                        2, 
                        tenant.Id
                        );
                    _logger.LogInformation("Sent 2-day reminder to {Email}", adminEmail);
                }

                // Trial expired
                if (daysLeft <= 0 && !tenant.IsSuspended)
                {
                    tenant.SubscriptionStatus = SubscriptionStatus.Canceled;
                    tenant.IsSuspended = true;
                    await emailService.SendTrialExpiredAsync(
                        adminEmail,
                        tenant.Name,
                        tenant.Subdomain,
                        tenant.Id
                        );
                    _logger.LogInformation("Trial expired for {TenantName}, account suspended", tenant.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tenant {TenantId}", tenant.Id);
            }
        }

        await db.SaveChangesAsync();
    }
}
