using Clienta.Api.Data;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.BackgroundServices;

/// <summary>
/// Background service that checks for expired grace periods and suspends accounts
/// Runs daily to enforce payment grace periods
/// </summary>
public class GracePeriodService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GracePeriodService> _logger;

    public GracePeriodService(
        IServiceScopeFactory scopeFactory,
        ILogger<GracePeriodService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Grace Period Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckGracePeriods();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking grace periods");
            }

            // Check every 6 hours
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private async Task CheckGracePeriods()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var now = DateTime.UtcNow;

        // Find tenants with expired grace periods
        var expiredTenants = await db.Tenants
            .Where(t => t.PaymentGracePeriodEndsAt.HasValue 
                     && t.PaymentGracePeriodEndsAt < now 
                     && !t.IsSuspended)
            .ToListAsync();

        _logger.LogInformation("Found {Count} tenants with expired grace periods", expiredTenants.Count);

        foreach (var tenant in expiredTenants)
        {
            try
            {
                _logger.LogWarning("Suspending tenant {TenantId} ({Name}) - grace period expired", 
                    tenant.Id, tenant.Name);

                // Suspend the account
                tenant.SubscriptionStatus = SubscriptionStatus.Unpaid;
                tenant.IsSuspended = true;
                
                await db.SaveChangesAsync();

                // Get admin email
                var adminEmail = await db.Users
                    .Where(u => u.TenantId == tenant.Id && u.Role == "Admin")
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrEmpty(adminEmail))
                {
                    // Send suspension email
                    await emailService.SendEmailAsync(
                        adminEmail,
                        "Account Suspended - Payment Required",
                        GetSuspensionEmailBody(tenant)
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error suspending tenant {TenantId}", tenant.Id);
            }
        }
    }

    private string GetSuspensionEmailBody(Entities.Tenant tenant)
    {
        return $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <h2 style='color: #d32f2f;'>⚠️ Account Suspended</h2>
                <p>Hi {tenant.Name} team,</p>
                <p>Your account has been suspended due to a payment issue.</p>
                <p><strong>Your 3-day grace period has expired.</strong></p>
                
                <h3>What happens now?</h3>
                <ul>
                    <li>Your account is temporarily suspended</li>
                    <li>You cannot access your data or services</li>
                    <li>Your data is safe and will be retained</li>
                </ul>

                <h3>How to restore access:</h3>
                <ol>
                    <li>Log in to your billing dashboard</li>
                    <li>Update your payment method</li>
                    <li>Your account will be restored immediately</li>
                </ol>

                <p>
                    <a href='https://{tenant.Subdomain}.yourapp.com/billing' 
                       style='display: inline-block; padding: 12px 24px; background: #1976d2; color: white; text-decoration: none; border-radius: 4px;'>
                        Update Payment Method
                    </a>
                </p>

                <p style='color: #666; font-size: 14px; margin-top: 30px;'>
                    Questions? Reply to this email or contact support.
                </p>
            </body>
            </html>
        ";
    }
}
