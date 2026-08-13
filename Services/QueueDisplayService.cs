using System.Security.Cryptography;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IQueueDisplayService
{
    Task<QueueDisplaySettings> GetOrCreateSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<QueueDisplaySettings> RegenerateTokenAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<QueueDisplayDto?> GetPublicDisplayAsync(string token, Guid? departmentId = null, CancellationToken cancellationToken = default);
    Task<QueueDisplayDto?> GetDisplayForTenantAsync(Guid tenantId, Guid? departmentId = null, CancellationToken cancellationToken = default);
}

public class QueueDisplayService : IQueueDisplayService
{
    private sealed record QueueSnapshotRow(
        Guid Id,
        int? QueueNumber,
        string Status,
        string ClientName,
        DateTime StartTime
    );

    private readonly AppDbContext _db;

    public QueueDisplayService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<QueueDisplaySettings> GetOrCreateSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.QueueDisplaySettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        if (existing != null)
            return existing;

        var settings = new QueueDisplaySettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PublicToken = await CreateUniquePublicTokenAsync(cancellationToken),
            PrivacyMode = QueueDisplayPrivacyMode.FullName,
            Theme = QueueDisplayTheme.Default,
            UpdatedAt = DateTime.UtcNow
        };

        _db.QueueDisplaySettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken);

        return settings;
    }

    public async Task<QueueDisplaySettings> RegenerateTokenAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateSettingsAsync(tenantId, cancellationToken);
        settings.PublicToken = await CreateUniquePublicTokenAsync(cancellationToken);
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task<QueueDisplayDto?> GetPublicDisplayAsync(string token, Guid? departmentId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var settings = await _db.QueueDisplaySettings
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.PublicToken == token, cancellationToken);

        if (settings == null)
            return null;

        var features = await _db.TenantFeatures
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.TenantId == settings.TenantId, cancellationToken);

        if (features?.QueueDisplayEnabled != true)
            return null;

        return await BuildDtoAsync(settings.TenantId, settings, settings.Tenant?.Name, settings.Tenant?.LogoUrl, departmentId, cancellationToken);
    }

    public async Task<QueueDisplayDto?> GetDisplayForTenantAsync(Guid tenantId, Guid? departmentId = null, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateSettingsAsync(tenantId, cancellationToken);
        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Name, t.LogoUrl })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant == null)
            return null;

        return await BuildDtoAsync(tenantId, settings, tenant.Name, tenant.LogoUrl, departmentId, cancellationToken);
    }

    private async Task<QueueDisplayDto> BuildDtoAsync(
        Guid tenantId,
        QueueDisplaySettings settings,
        string? businessName,
        string? tenantLogoUrl,
        Guid? departmentId,
        CancellationToken cancellationToken)
    {
        // departmentId is reserved for future per-department queue display filtering.
        // Current behavior remains tenant-wide.
        var queueRows = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Client)
            .Where(a => a.TenantId == tenantId
                && (a.Status == AppointmentStatuses.InProgress || a.Status == AppointmentStatuses.Waiting))
            .OrderBy(a => a.QueueNumber ?? int.MaxValue)
            .ThenBy(a => a.StartTime)
            .Select(a => new QueueSnapshotRow(
                a.Id,
                a.QueueNumber,
                a.Status,
                a.Client.FullName,
                a.StartTime))
            .ToListAsync(cancellationToken);

        var inProgress = queueRows
            .Where(a => a.Status == AppointmentStatuses.InProgress)
            .OrderBy(a => a.QueueNumber ?? int.MaxValue)
            .ThenBy(a => a.StartTime)
            .FirstOrDefault();

        var nextWaiting = queueRows
            .Where(a => a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber ?? int.MaxValue)
            .ThenBy(a => a.StartTime)
            .FirstOrDefault();

        var waitingCount = queueRows.Count(a => a.Status == AppointmentStatuses.Waiting);
        var generatedAt = DateTime.UtcNow;
        var advertisementImages = await _db.QueueDisplayAdvertisementImages
            .AsNoTracking()
            .Where(ad => ad.TenantId == tenantId && ad.QueueDisplaySettingsId == settings.Id)
            .OrderBy(ad => ad.DisplayOrder)
            .ThenBy(ad => ad.CreatedAt)
            .Select(ad => new QueueDisplayAdvertisementImageDto(ad.Id, ad.ImageUrl, ad.DisplayOrder))
            .ToListAsync(cancellationToken);

        return new QueueDisplayDto(
            businessName ?? "Clienta",
            settings.LogoOverrideUrl ?? tenantLogoUrl,
            settings.Theme,
            settings.PrivacyMode,
            advertisementImages,
            waitingCount,
            ToQueuePatient(inProgress, settings.PrivacyMode),
            ToQueuePatient(nextWaiting, settings.PrivacyMode),
            settings.UpdatedAt,
            generatedAt.Ticks.ToString(),
            generatedAt
        );
    }

    private static QueueDisplayPatientDto? ToQueuePatient(QueueSnapshotRow? appointment, QueueDisplayPrivacyMode privacyMode)
    {
        if (appointment == null)
            return null;

        var fullName = appointment.ClientName ?? "Patient";
        var firstName = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;
        var ticketLabel = $"Ticket {(appointment.QueueNumber is int qn ? qn.ToString() : "-")}";

        var displayName = privacyMode switch
        {
            QueueDisplayPrivacyMode.FullName => fullName,
            QueueDisplayPrivacyMode.FirstNameOnly => firstName,
            QueueDisplayPrivacyMode.QueueNumberOnly => ticketLabel,
            _ => fullName
        };

        return new QueueDisplayPatientDto(
            appointment.Id,
            appointment.QueueNumber,
            displayName,
            appointment.Status
        );
    }

    private async Task<string> CreateUniquePublicTokenAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var token = CreatePublicToken();
            var exists = await _db.QueueDisplaySettings
                .AsNoTracking()
                .AnyAsync(s => s.PublicToken == token, cancellationToken);

            if (!exists)
                return token;
        }

        throw new InvalidOperationException("Could not generate a unique queue display token.");
    }

    private static string CreatePublicToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
