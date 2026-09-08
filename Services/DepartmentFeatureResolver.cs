using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IDepartmentFeatureResolver
{
    Task<bool> IsFeatureEnabledAsync(Guid tenantId, Guid? departmentId, string featureKey, CancellationToken cancellationToken = default);
    Task<List<DepartmentFeatureItemResponse>> GetDepartmentFeaturesAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default);
    Task UpdateDepartmentFeaturesAsync(Guid tenantId, Guid departmentId, IReadOnlyCollection<DepartmentFeatureUpdateItem> updates, CancellationToken cancellationToken = default);
}

public class DepartmentFeatureResolver : IDepartmentFeatureResolver
{
    private static readonly string[] SupportedFeatureKeys =
    [
        "quotesEnabled",
        "prescriptionsEnabled",
        "drugsEnabled",
        "consentFormsEnabled",
        "visitSummariesEnabled",
        "beforeAfterPhotosEnabled",
        "teamChatEnabled",
        "whatsAppEnabled",
        "notDocumentedEnabled",
        "medicalImagingEnabled"
    ];

    private readonly AppDbContext _context;

    public DepartmentFeatureResolver(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsFeatureEnabledAsync(Guid tenantId, Guid? departmentId, string featureKey, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeFeatureKey(featureKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
            return true;

        var tenantFeatures = await GetTenantFeaturesAsync(tenantId, cancellationToken);
        var tenantEnabled = IsTenantFeatureEnabled(tenantFeatures, normalizedKey);
        if (!tenantEnabled)
            return false;

        if (!departmentId.HasValue)
            return true;

        var hasAnyOverrides = await _context.DepartmentFeatures
            .AnyAsync(df => df.DepartmentId == departmentId.Value, cancellationToken);

        if (!hasAnyOverrides)
            return true;

        var overrideEntry = await _context.DepartmentFeatures
            .AsNoTracking()
            .FirstOrDefaultAsync(df => df.DepartmentId == departmentId.Value && df.FeatureKey == normalizedKey, cancellationToken);

        return overrideEntry?.IsEnabled ?? true;
    }

    public async Task<List<DepartmentFeatureItemResponse>> GetDepartmentFeaturesAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default)
    {
        var tenantFeatures = await GetTenantFeaturesAsync(tenantId, cancellationToken);

        var existing = await _context.DepartmentFeatures
            .AsNoTracking()
            .Where(df => df.DepartmentId == departmentId)
            .ToDictionaryAsync(df => df.FeatureKey, df => df.IsEnabled, cancellationToken);

        var hasAnyOverrides = existing.Count > 0;

        return SupportedFeatureKeys
            .Select(key =>
            {
                var tenantEnabled = IsTenantFeatureEnabled(tenantFeatures, key);
                var departmentEnabled = hasAnyOverrides
                    ? (existing.TryGetValue(key, out var value) ? value : true)
                    : true;

                return new DepartmentFeatureItemResponse(
                    key,
                    tenantEnabled,
                    departmentEnabled,
                    tenantEnabled && departmentEnabled
                );
            })
            .ToList();
    }

    public async Task UpdateDepartmentFeaturesAsync(Guid tenantId, Guid departmentId, IReadOnlyCollection<DepartmentFeatureUpdateItem> updates, CancellationToken cancellationToken = default)
    {
        var allowedKeys = new HashSet<string>(SupportedFeatureKeys, StringComparer.OrdinalIgnoreCase);
        var normalizedUpdates = updates
            .Where(update => update != null)
            .Select(update => new DepartmentFeatureUpdateItem(NormalizeFeatureKey(update.FeatureKey), update.IsEnabled))
            .Where(update => !string.IsNullOrWhiteSpace(update.FeatureKey) && allowedKeys.Contains(update.FeatureKey))
            .GroupBy(update => update.FeatureKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        var existing = await _context.DepartmentFeatures
            .Where(df => df.DepartmentId == departmentId)
            .ToListAsync(cancellationToken);

        if (normalizedUpdates.Count == 0)
        {
            if (existing.Count > 0)
            {
                _context.DepartmentFeatures.RemoveRange(existing);
                await _context.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        var existingMap = existing.ToDictionary(df => df.FeatureKey, StringComparer.OrdinalIgnoreCase);

        foreach (var update in normalizedUpdates)
        {
            if (existingMap.TryGetValue(update.FeatureKey, out var current))
            {
                current.IsEnabled = update.IsEnabled;
                continue;
            }

            _context.DepartmentFeatures.Add(new DepartmentFeature
            {
                Id = Guid.NewGuid(),
                DepartmentId = departmentId,
                FeatureKey = update.FeatureKey,
                IsEnabled = update.IsEnabled,
            });
        }

        var desiredKeys = normalizedUpdates.Select(update => update.FeatureKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var staleEntries = existing.Where(df => !desiredKeys.Contains(df.FeatureKey)).ToList();
        if (staleEntries.Count > 0)
        {
            _context.DepartmentFeatures.RemoveRange(staleEntries);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<TenantFeatures> GetTenantFeaturesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var features = await _context.TenantFeatures
            .AsNoTracking()
            .FirstOrDefaultAsync(tf => tf.TenantId == tenantId, cancellationToken);

        return features ?? new TenantFeatures
        {
            TenantId = tenantId,
            ReportsEnabled = true,
            InvoicesEnabled = true,
            QuotesEnabled = false,
            PrescriptionsEnabled = false,
            DrugsEnabled = false,
            BeforeAfterPhotosEnabled = true,
            VisitSummariesEnabled = false,
            TeamChatEnabled = false,
            WhatsAppEnabled = false,
            NotDocumentedEnabled = true,
            MedicalImagingEnabled = false,
        };
    }

    private static string NormalizeFeatureKey(string? featureKey)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
            return string.Empty;

        return featureKey.Trim();
    }

    private static bool IsTenantFeatureEnabled(TenantFeatures tenantFeatures, string featureKey)
    {
        return featureKey switch
        {
            "quotesEnabled" => tenantFeatures.QuotesEnabled,
            "prescriptionsEnabled" => tenantFeatures.PrescriptionsEnabled,
            "drugsEnabled" => tenantFeatures.DrugsEnabled,
            "consentFormsEnabled" => true,
            "visitSummariesEnabled" => tenantFeatures.VisitSummariesEnabled,
            "beforeAfterPhotosEnabled" => tenantFeatures.BeforeAfterPhotosEnabled,
            "teamChatEnabled" => tenantFeatures.TeamChatEnabled,
            "whatsAppEnabled" => tenantFeatures.WhatsAppEnabled,
            "notDocumentedEnabled" => tenantFeatures.NotDocumentedEnabled,
            "medicalImagingEnabled" => tenantFeatures.MedicalImagingEnabled,
            _ => true,
        };
    }
}
