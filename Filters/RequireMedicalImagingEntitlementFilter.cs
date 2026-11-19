using Clienta.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Clienta.Api.Filters;

public sealed class RequireMedicalImagingEntitlementFilter : IAsyncActionFilter
{
    private readonly IFeatureService _featureService;
    private readonly ITenantContext _tenantContext;

    public RequireMedicalImagingEntitlementFilter(
        IFeatureService featureService,
        ITenantContext tenantContext)
    {
        _featureService = featureService;
        _tenantContext = tenantContext;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "tenant_not_resolved" });
            return;
        }

        if (!await _featureService.IsEnabledAsync("medicalImaging"))
        {
            context.Result = new ObjectResult(new { error = "medical_imaging_unavailable" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
