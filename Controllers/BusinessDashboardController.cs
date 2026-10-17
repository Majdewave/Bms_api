using Clienta.Api.DTOs;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

// Phase 8A - CLIENTA Business financial dashboard. Separate from the existing /api/dashboard, which is untouched.
[ApiController]
[Route("api/business/dashboard")]
[Authorize(Policy = "manage_projects")]
public class BusinessDashboardController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantProductService _productService;
    private readonly IPmAccessService _pmAccessService;
    private readonly IBusinessDashboardService _dashboardService;
    private readonly IAuthorizationService _authorizationService;

    public BusinessDashboardController(
        ITenantContext tenantContext,
        ITenantProductService productService,
        IPmAccessService pmAccessService,
        IBusinessDashboardService dashboardService,
        IAuthorizationService authorizationService)
    {
        _tenantContext = tenantContext;
        _productService = productService;
        _pmAccessService = pmAccessService;
        _dashboardService = dashboardService;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        // Legacy, Clinic and unknown all fail closed.
        if (await _productService.GetAsync(tenantId, cancellationToken) != ResolvedTenantProduct.Business)
            return StatusCode(StatusCodes.Status403Forbidden, "The Business dashboard is not available for this tenant.");

        if (!await _pmAccessService.IsPmEnabledAsync(tenantId, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "PM feature is not enabled for this tenant.");

        // Invoice-derived totals additionally need invoice access (permission + feature), as on the PM payment summaries.
        var invoiceAccess = (await _authorizationService.AuthorizeAsync(User, "manage_invoices")).Succeeded
            && await _dashboardService.IsInvoicesEnabledAsync(tenantId, cancellationToken);

        return Ok(await _dashboardService.GetAsync(tenantId, invoiceAccess, cancellationToken: cancellationToken));
    }
}
