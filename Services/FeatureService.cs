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

    public async Task<bool> IsEnabledAsync(string feature)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return true;

        var features = await GetAsync();

        return feature.Trim().ToLowerInvariant() switch
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
