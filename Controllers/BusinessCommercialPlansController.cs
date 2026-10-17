using Clienta.Api.DTOs;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/business/plans")]
[AllowAnonymous]
public sealed class BusinessCommercialPlansController : ControllerBase
{
    private readonly IBusinessCommercialPlanCatalog _catalog;

    public BusinessCommercialPlansController(IBusinessCommercialPlanCatalog catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BusinessCommercialPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEnabledBusinessPlans(CancellationToken cancellationToken)
    {
        var plans = await _catalog.GetAllAsync(cancellationToken);
        return Ok(plans
            .Where(plan => plan.Configuration.Enabled)
            .Select(BusinessCommercialPlanDtoMapper.ToDto)
            .ToArray());
    }
}