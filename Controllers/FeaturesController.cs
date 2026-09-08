using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/features")]
[Authorize]
public class FeaturesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IFeatureService _featureService;
    private readonly ITenantContext _tenantContext;
    private readonly IUserDepartmentFeatureAccessService _userDepartmentFeatureAccessService;

    public FeaturesController(
        AppDbContext context,
        IFeatureService featureService,
        ITenantContext tenantContext,
        IUserDepartmentFeatureAccessService userDepartmentFeatureAccessService)
    {
        _context = context;
        _featureService = featureService;
        _tenantContext = tenantContext;
        _userDepartmentFeatureAccessService = userDepartmentFeatureAccessService;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var features = await _featureService.GetAsync();
        var dto = new UpdateTenantFeaturesRequest(
            features.ReportsEnabled,
            features.InvoicesEnabled,
            features.QuotesEnabled,
            features.PrescriptionsEnabled,
            features.DrugsEnabled,
            features.BeforeAfterPhotosEnabled,
            features.VisitSummariesEnabled,
            features.TeamChatEnabled,
            features.QueueDisplayEnabled,
            features.WhatsAppEnabled,
            features.NotDocumentedEnabled,
            features.MedicalImagingEnabled
        );
        return Ok(dto);
    }

    [HttpGet("effective")]
    public async Task<IActionResult> GetEffective()
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var effectiveFeatures = await _userDepartmentFeatureAccessService.GetCurrentUserEffectiveFeaturesAsync();
        return Ok(effectiveFeatures);
    }

    // Department-scoped check, distinct from the user-aggregate GET("effective") above.
    [HttpGet("effective/{featureKey}")]
    public async Task<IActionResult> GetEffectiveForFeature(string featureKey, [FromQuery] Guid? departmentId)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var enabled = await _userDepartmentFeatureAccessService.IsFeatureEnabledAsync(departmentId, featureKey);
        return Ok(new { enabled });
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(UpdateTenantFeaturesRequest request)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var features = await _featureService.GetAsync();

        features.ReportsEnabled = request.ReportsEnabled;
        features.InvoicesEnabled = request.InvoicesEnabled;
        features.QuotesEnabled = request.QuotesEnabled;
        features.PrescriptionsEnabled = request.PrescriptionsEnabled;
        features.DrugsEnabled = request.DrugsEnabled;
        features.BeforeAfterPhotosEnabled = request.BeforeAfterPhotosEnabled;
        features.VisitSummariesEnabled = request.VisitSummariesEnabled;
        features.TeamChatEnabled = request.TeamChatEnabled;
        features.QueueDisplayEnabled = request.QueueDisplayEnabled;
        features.WhatsAppEnabled = request.WhatsAppEnabled;
        features.NotDocumentedEnabled = request.NotDocumentedEnabled;
        features.MedicalImagingEnabled = request.MedicalImagingEnabled;

        await _context.SaveChangesAsync();

        return Ok(features);
    }
}
