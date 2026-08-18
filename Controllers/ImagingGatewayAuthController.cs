using Clienta.Api.Authorization;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging")]
public class ImagingGatewayAuthController : ControllerBase
{
    private readonly ITenantContext _tenantContext;

    public ImagingGatewayAuthController(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    [HttpGet("auth-test")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.ImagingGatewayScheme, Policy = PlatformAuthConstants.PolicyImagingGateway)]
    public IActionResult AuthTest()
    {
        var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
        var credentialIdClaim = User.Claims.FirstOrDefault(c => c.Type == "gateway_credential_id")?.Value;
        var keyIdClaim = User.Claims.FirstOrDefault(c => c.Type == "gateway_key_id")?.Value;

        return Ok(new
        {
            authenticated = true,
            tenantIdFromClaim = tenantIdClaim,
            tenantIdFromContext = _tenantContext.TenantId,
            gatewayCredentialId = credentialIdClaim,
            gatewayKeyId = keyIdClaim,
            utcNow = DateTime.UtcNow
        });
    }
}
