using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IFeatureService
{
    Task<TenantFeatures> GetAsync();
    Task<bool> IsEnabledAsync(string feature);
}

public class FeatureService : IFeatureService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;

    public FeatureService(AppDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<TenantFeatures> GetAsync()
    {
        var tenantId = _tenantContext.TenantId;

        if (tenantId == Guid.Empty)
        {
            return new TenantFeatures
            {
                TenantId = Guid.Empty,
                ReportsEnabled = true,
                InvoicesEnabled = true,
                PrescriptionsEnabled = false,
                DrugsEnabled = false,
                BeforeAfterPhotosEnabled = true
            };
        }

        var features = await _db.TenantFeatures
            .FirstOrDefaultAsync(f => f.TenantId == tenantId);

        if (features != null)
            return features;

        features = new TenantFeatures
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReportsEnabled = true,
            InvoicesEnabled = true,
            PrescriptionsEnabled = false,
            DrugsEnabled = false,
            BeforeAfterPhotosEnabled = true
        };

        _db.TenantFeatures.Add(features);
        await _db.SaveChangesAsync();

        return features;
    }

    public async Task<bool> IsEnabledAsync(string feature)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return true;

        var features = await GetAsync();

        return feature.Trim().ToLowerInvariant() switch
        {
            "reports" => features.ReportsEnabled,
            "invoices" => features.InvoicesEnabled,
            "prescriptions" => features.PrescriptionsEnabled,
            "drugs" => features.DrugsEnabled,
            "beforeafterphotos" => features.BeforeAfterPhotosEnabled,
            "before_after_photos" => features.BeforeAfterPhotosEnabled,
            _ => true
        };
    }
}
