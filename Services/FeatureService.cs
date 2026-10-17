using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IFeatureService
{
    Task<TenantFeatures> GetAsync();
    Task<TenantFeatures> GetEffectiveAsync();
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
                QuotesEnabled = false,
                PrescriptionsEnabled = false,
                DrugsEnabled = false,
                BeforeAfterPhotosEnabled = true,
                TeamChatEnabled = false,
                QueueDisplayEnabled = false,
                WhatsAppEnabled = false,
                NotDocumentedEnabled = true,
                MedicalImagingEnabled = false
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
            QuotesEnabled = false,
            PrescriptionsEnabled = false,
            DrugsEnabled = false,
            BeforeAfterPhotosEnabled = true,
            TeamChatEnabled = false,
            QueueDisplayEnabled = false,
            WhatsAppEnabled = false,
            NotDocumentedEnabled = true,
            MedicalImagingEnabled = false
        };

        _db.TenantFeatures.Add(features);
        await _db.SaveChangesAsync();

        return features;
    }

    public async Task<TenantFeatures> GetEffectiveAsync()
    {
        var features = await GetAsync();
        if (_tenantContext.TenantId == Guid.Empty)
            return features;

        var businessCapabilitiesEnabled = await _db.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == _tenantContext.TenantId)
            .Select(tenant => BusinessPlanEntitlement.HasCurrentBusinessCapabilities(tenant.ProductType, tenant.Plan))
            .FirstOrDefaultAsync();

        if (!businessCapabilitiesEnabled)
            return features;

        // Return a detached view model so presenting entitlements never mutates or persists legacy feature flags.
        return new TenantFeatures
        {
            Id = features.Id,
            TenantId = features.TenantId,
            ReportsEnabled = features.ReportsEnabled,
            InvoicesEnabled = true,
            QuotesEnabled = true,
            PrescriptionsEnabled = features.PrescriptionsEnabled,
            DrugsEnabled = features.DrugsEnabled,
            BeforeAfterPhotosEnabled = features.BeforeAfterPhotosEnabled,
            VisitSummariesEnabled = features.VisitSummariesEnabled,
            TeamChatEnabled = features.TeamChatEnabled,
            QueueDisplayEnabled = features.QueueDisplayEnabled,
            WhatsAppEnabled = features.WhatsAppEnabled,
            NotDocumentedEnabled = features.NotDocumentedEnabled,
            MedicalImagingEnabled = features.MedicalImagingEnabled,
            PmEnabled = features.PmEnabled,
        };
    }

    public async Task<bool> IsEnabledAsync(string feature)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return true;

        var normalizedFeature = feature.Trim().ToLowerInvariant();
        if (normalizedFeature is "quotes" or "quotesenabled" or "quotes_enabled" or "invoices" or "invoicesenabled" or "invoices_enabled")
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Where(item => item.Id == _tenantContext.TenantId)
                .Select(item => new { item.ProductType, item.Plan })
                .FirstOrDefaultAsync();

            if (tenant is not null && BusinessPlanEntitlement.HasCurrentBusinessCapabilities(tenant.ProductType, tenant.Plan))
                return true;
        }

        var features = await GetAsync();

        return normalizedFeature switch
        {
            "reports" => features.ReportsEnabled,
            "invoices" => features.InvoicesEnabled,
            "quotes" => features.QuotesEnabled,
            "quotesenabled" => features.QuotesEnabled,
            "quotes_enabled" => features.QuotesEnabled,
            "prescriptions" => features.PrescriptionsEnabled,
            "drugs" => features.DrugsEnabled,
            "beforeafterphotos" => features.BeforeAfterPhotosEnabled,
            "before_after_photos" => features.BeforeAfterPhotosEnabled,
            "teamchat" => features.TeamChatEnabled,
            "team_chat" => features.TeamChatEnabled,
            "queuedisplay" => features.QueueDisplayEnabled,
            "queue_display" => features.QueueDisplayEnabled,
            "queuedisplayenabled" => features.QueueDisplayEnabled,

            "whatsapp" => features.WhatsAppEnabled,
            "whatsappenabled" => features.WhatsAppEnabled,
            "whatsapp_enabled" => features.WhatsAppEnabled,

            "notdocumented" => features.NotDocumentedEnabled,
            "notdocumentedenabled" => features.NotDocumentedEnabled,
            "not_documented" => features.NotDocumentedEnabled,

            "medicalimaging" => features.MedicalImagingEnabled,
            "medicalimagingenabled" => features.MedicalImagingEnabled,
            "medical_imaging" => features.MedicalImagingEnabled,

            _ => true
        };
    }
}
