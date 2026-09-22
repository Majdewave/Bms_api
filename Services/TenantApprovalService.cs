using Clienta.Api.Data;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Services.Platform;

namespace Clienta.Api.Services;

public interface ITenantApprovalService
{
    Task ApproveTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}

public class TenantApprovalService : ITenantApprovalService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IOnboardingLocalizationService _localization;
    private readonly IConfiguration _config;
    private readonly IPlatformSettingsService _platformSettingsService;

    public TenantApprovalService(
        AppDbContext db,
        IEmailService emailService,
        IOnboardingLocalizationService localization,
        IConfiguration config,
        IPlatformSettingsService platformSettingsService)
    {
        _db = db;
        _emailService = emailService;
        _localization = localization;
        _config = config;
        _platformSettingsService = platformSettingsService;
    }

    public async Task ApproveTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant == null)
        {
            throw new InvalidOperationException("Tenant not found.");
        }

        if (tenant.SubscriptionStatus != SubscriptionStatus.PendingApproval)
        {
            throw new InvalidOperationException("Tenant is not pending approval.");
        }

        var platformSettings = await _platformSettingsService.GetAsync(cancellationToken);
        var trialDays = platformSettings.DefaultTrialDays;

        if (trialDays <= 0)
        {
            throw new InvalidOperationException("Platform DefaultTrialDays must be greater than zero.");
        }

        var trialStart = DateTime.UtcNow;
        tenant.SubscriptionStatus = SubscriptionStatus.Trialing;
        tenant.TrialStartsAt = trialStart;
        tenant.TrialEndsAt = trialStart.AddDays(trialDays);
        tenant.IsTrial = true;
        tenant.IsSuspended = false;

        await _db.SaveChangesAsync(cancellationToken);

        var admin = await _db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenant.Id && u.Role == "Admin")
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (admin == null || string.IsNullOrWhiteSpace(admin.Email))
        {
            return;
        }

        var language = _localization.ResolveLanguage(tenant.PreferredLanguage, null);
        var baseUrl = _config["App:BaseUrl"] ?? "https://clienta.digitalpenpro.com";
        var loginUrl = $"{baseUrl.TrimEnd('/')}/login";
        var email = _localization.BuildAccountApprovedEmail(language, admin.FullName ?? string.Empty, loginUrl, tenant.TrialEndsAt!.Value);

        await _emailService.SendEmailAsync(admin.Email, email.Subject, email.HtmlBody);
    }
}
