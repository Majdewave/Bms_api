using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using Clienta.Api.Services;

public class TrialService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _emailService;
    private readonly ILogger<TrialService> _logger;

    public TrialService(AppDbContext db, IEmailService emailService, ILogger<TrialService> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task ProcessTrialsAsync()
    {
        var lockAcquired = await _db.Database.ExecuteSqlRawAsync("SELECT pg_try_advisory_lock(123456);");
        if (lockAcquired == 0)
        {
            _logger.LogInformation("Trial job skipped - another instance is running");
            return;
        }

        try
        {
            var now = DateTime.UtcNow;

            // ======================
            // Reminder (יום לפני)
            // ======================
            var reminderThreshold = now.AddDays(1);

            var tenantsForReminder = await _db.Tenants
                .Where(t =>
                    t.TrialEndsAt != null &&
                    t.StripeSubscriptionId == null &&
                    t.TrialEndsAt <= reminderThreshold &&
                    t.TrialEndsAt > now &&
                    !t.TrialReminderSent)
                .ToListAsync();

            foreach (var tenant in tenantsForReminder)
            {
                try
                {
                    var user = await _db.Users
                        .Where(u => u.TenantId == tenant.Id && u.Role == "Admin")
                        .FirstOrDefaultAsync();

                    if (user == null || string.IsNullOrEmpty(user.Email))
                    {
                        _logger.LogWarning("No admin email for tenant {TenantId}", tenant.Id);
                        continue;
                    }

                    await _emailService.SendTrialReminderAsync(
                        user.Email,
                        tenant.Name,
                        tenant.Subdomain,
                        Math.Max(0, (tenant.TrialEndsAt.Value - DateTime.UtcNow).Days),
                        tenant.Id
                    );
                    tenant.TrialReminderSent = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reminder failed {TenantId}", tenant.Id);
                }
            }

            // ======================
            // Expired
            // ======================
            var tenantsExpired = await _db.Tenants
                .Where(t =>
                    t.TrialEndsAt != null &&
                    t.StripeSubscriptionId == null &&
                    t.TrialEndsAt < now &&
                    !t.TrialExpiredSent)
                .ToListAsync();

            foreach (var tenant in tenantsExpired)
            {
                try
                {
                    var user = await _db.Users
    .Where(u => u.TenantId == tenant.Id && u.Role == "Admin")
    .FirstOrDefaultAsync();

                    if (user == null || string.IsNullOrEmpty(user.Email))
                    {
                        _logger.LogWarning("No admin email for tenant {TenantId}", tenant.Id);
                        continue;
                    }

                    await _emailService.SendTrialExpiredAsync(
                        user.Email,
                        tenant.Name,
                        tenant.Subdomain,
                        tenant.Id
                    );

                    tenant.TrialExpiredSent = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Expired failed {TenantId}", tenant.Id);
                }
            }

            await _db.SaveChangesAsync();
        }
        finally
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(123456);");
        }
    }
}
