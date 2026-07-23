using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services.Platform;

public interface IPlatformSettingsService
{
    Task<PlatformSettingsDto> GetAsync(CancellationToken cancellationToken);
    Task<PlatformSettingsDto> UpdateAsync(UpdatePlatformSettingsRequest request, CancellationToken cancellationToken);
    Task<PlatformSettingsDto> GetPublicAsync(CancellationToken cancellationToken);
}

public sealed class PlatformSettingsService : IPlatformSettingsService
{
    private readonly MasterDbContext _masterDb;

    public PlatformSettingsService(MasterDbContext masterDb)
    {
        _masterDb = masterDb;
    }

    public async Task<PlatformSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await EnsureSettingsAsync(cancellationToken);
        return Map(settings);
    }

    public async Task<PlatformSettingsDto> GetPublicAsync(CancellationToken cancellationToken)
        => await GetAsync(cancellationToken);

    public async Task<PlatformSettingsDto> UpdateAsync(UpdatePlatformSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await EnsureSettingsAsync(cancellationToken);

        settings.SupportEmail = request.SupportEmail.Trim();
        settings.SupportPhone = NormalizeOptional(request.SupportPhone);
        settings.WebsiteUrl = NormalizeOptional(request.WebsiteUrl);
        settings.DefaultTrialDays = request.DefaultTrialDays;
        settings.TrialReminderDays = request.TrialReminderDays;
        settings.AllowRegistrations = request.AllowRegistrations;
        settings.RequireManualApproval = request.RequireManualApproval;
        settings.EnableBilling = request.EnableBilling;
        settings.EnableHelpCenter = request.EnableHelpCenter;
        settings.WhatsAppEnabled = request.WhatsAppEnabled;
        settings.ProMonthlyPrice = request.ProMonthlyPrice;
        settings.ProAnnualPrice = request.ProAnnualPrice;
        settings.ProDescription = request.ProDescription.Trim();
        settings.ProEnabled = request.ProEnabled;
        settings.ProDisplayOrder = request.ProDisplayOrder;
        settings.UpdatedAt = DateTime.UtcNow;

        await _masterDb.SaveChangesAsync(cancellationToken);
        return Map(settings);
    }

    private async Task<PlatformSettings> EnsureSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _masterDb.Set<PlatformSettings>().FirstOrDefaultAsync(cancellationToken);
        if (settings != null)
        {
            return settings;
        }

        settings = new PlatformSettings
        {
            Id = Guid.NewGuid(),
        };

        _masterDb.Set<PlatformSettings>().Add(settings);
        await _masterDb.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static PlatformSettingsDto Map(PlatformSettings settings)
    {
        return new PlatformSettingsDto(
            settings.Id,
            settings.SupportEmail,
            settings.SupportPhone,
            settings.WebsiteUrl,
            settings.DefaultTrialDays,
            settings.TrialReminderDays,
            settings.AllowRegistrations,
            settings.RequireManualApproval,
            settings.EnableBilling,
            settings.EnableHelpCenter,
            settings.WhatsAppEnabled,
            settings.ProMonthlyPrice,
            settings.ProAnnualPrice,
            settings.ProDescription,
            settings.ProEnabled,
            settings.ProDisplayOrder,
            settings.UpdatedAt);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
