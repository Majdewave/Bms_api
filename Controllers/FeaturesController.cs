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

    public FeaturesController(AppDbContext context, IFeatureService featureService, ITenantContext tenantContext)
    {
        _context = context;
        _featureService = featureService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var features = await _featureService.GetAsync();
        return Ok(features);
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
        features.PrescriptionsEnabled = request.PrescriptionsEnabled;
        features.DrugsEnabled = request.DrugsEnabled;
        features.BeforeAfterPhotosEnabled = request.BeforeAfterPhotosEnabled;

        await _context.SaveChangesAsync();

        return Ok(features);
    }
}
