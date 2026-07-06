using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class CleanupService : BackgroundService
{
private readonly IServiceProvider _serviceProvider;
private readonly ILogger<CleanupService> _logger;

public CleanupService(IServiceProvider serviceProvider, ILogger<CleanupService> logger)
{
    _serviceProvider = serviceProvider;
    _logger = logger;
}

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        var now = DateTime.UtcNow;
        var nextRun = now.Date.AddDays(1);
        var delay = nextRun - now;

        await Task.Delay(delay, stoppingToken);

        var runStartedAt = DateTime.UtcNow;
        _logger.LogInformation("Cleanup Started (UTC)");

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenants = await context.Tenants.ToListAsync(stoppingToken);

        foreach (var tenant in tenants)
        {
            if (!tenant.EnableAutoDeleteNotDocumented)
                continue;

            var tenantStartedAt = DateTime.UtcNow;
            var days = tenant.AutoDeleteNotDocumentedAfterDays;
            var cutoffDate = DateTime.UtcNow.Date.AddDays(-days);

            var appointmentIdsToDelete = await context.Appointments
                .Where(a =>
                    a.TenantId == tenant.Id &&
                    !a.IsDocumented &&
                    a.StartTime.Date < cutoffDate)
                .Select(a => a.Id)
                .ToListAsync(stoppingToken);

            if (appointmentIdsToDelete.Count == 0)
            {
                _logger.LogInformation(
                    "Tenant {TenantId} | Appointments Deleted {AppointmentsDeleted} | Related Records Deleted {RelatedRecordsDeleted} | DurationMs {DurationMs}",
                    tenant.Id,
                    0,
                    0,
                    (DateTime.UtcNow - tenantStartedAt).TotalMilliseconds);
                continue;
            }

            var consentsToDelete = await context.ClientConsents
                .Where(c => c.TenantId == tenant.Id && appointmentIdsToDelete.Contains(c.AppointmentId))
                .ToListAsync(stoppingToken);

            var visitSummariesToDelete = await context.VisitSummaries
                .Where(v => v.TenantId == tenant.Id && v.AppointmentId.HasValue && appointmentIdsToDelete.Contains(v.AppointmentId.Value))
                .ToListAsync(stoppingToken);

            var appointmentsToDelete = await context.Appointments
                .Where(a => a.TenantId == tenant.Id && appointmentIdsToDelete.Contains(a.Id))
                .ToListAsync(stoppingToken);

            context.ClientConsents.RemoveRange(consentsToDelete);
            context.VisitSummaries.RemoveRange(visitSummariesToDelete);
            context.Appointments.RemoveRange(appointmentsToDelete);

            await context.SaveChangesAsync(stoppingToken);

            var relatedDeletedCount = consentsToDelete.Count + visitSummariesToDelete.Count;
            _logger.LogInformation(
                "Tenant {TenantId} | Appointments Deleted {AppointmentsDeleted} | Related Records Deleted {RelatedRecordsDeleted} | DurationMs {DurationMs}",
                tenant.Id,
                appointmentsToDelete.Count,
                relatedDeletedCount,
                (DateTime.UtcNow - tenantStartedAt).TotalMilliseconds);
        }

            _logger.LogInformation("Cleanup DurationMs {DurationMs}", (DateTime.UtcNow - runStartedAt).TotalMilliseconds);
    }
}
}
