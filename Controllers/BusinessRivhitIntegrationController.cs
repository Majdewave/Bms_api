using Clienta.Api.Authorization;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Clienta.Api.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

// Phase 1 exposes the Rivhit integration to explicit Business tenants only; Legacy (null), Clinic and unknown fail closed.
[ApiController]
[Route("api/business/integrations/rivhit")]
[Authorize(Policy = IntegrationAuthorization.ManagePolicy)]
public class BusinessRivhitIntegrationController : ControllerBase
{
    private const IntegrationProvider Provider = IntegrationProvider.Rivhit;

    private readonly ITenantContext _tenantContext;
    private readonly ITenantProductService _productService;
    private readonly ITenantIntegrationService _integrations;

    public BusinessRivhitIntegrationController(
        ITenantContext tenantContext,
        ITenantProductService productService,
        ITenantIntegrationService integrations)
    {
        _tenantContext = tenantContext;
        _productService = productService;
        _integrations = integrations;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var denied = await GuardAsync(ct);
        if (denied is not null) return denied;
        return Ok(await _integrations.GetStateAsync(_tenantContext.TenantId, Provider, ct));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateIntegrationRequest request, CancellationToken ct)
    {
        var denied = await GuardAsync(ct);
        if (denied is not null) return denied;

        var result = await _integrations.UpdateAsync(_tenantContext.TenantId, Provider, request ?? new UpdateIntegrationRequest(), ct);
        return result.Outcome switch
        {
            IntegrationOutcome.Ok => Ok(result.State),
            IntegrationOutcome.InvalidToken => BadRequest(new { code = "InvalidToken", message = "The API token is not valid." }),
            IntegrationOutcome.NotConfigured => BadRequest(new { code = "NotConfigured", message = "Save an API token before enabling the integration." }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "SecretStorageUnavailable", message = "Secure credential storage is not available." })
        };
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        var denied = await GuardAsync(ct);
        if (denied is not null) return denied;

        var result = await _integrations.TestConnectionAsync(_tenantContext.TenantId, Provider, ct);
        if (result.Outcome == IntegrationOutcome.NotConfigured)
            return BadRequest(new { code = "NotConfigured", message = "Save an API token before testing the connection." });

        return Ok(new { success = result.Success, failureCode = result.FailureCode, state = result.State });
    }

    [HttpGet("metadata")]
    public async Task<IActionResult> GetMetadata(CancellationToken ct)
    {
        var denied = await GuardAsync(ct);
        if (denied is not null) return denied;

        var result = await _integrations.GetMetadataAsync(_tenantContext.TenantId, Provider, ct);
        return result.Outcome switch
        {
            IntegrationMetadataOutcome.Ok => Ok(new
            {
                paymentTypes = result.PaymentTypes,
                receiptTypes = result.ReceiptTypes
            }),
            IntegrationMetadataOutcome.NotConfigured => Conflict(new { code = "IntegrationUnavailable" }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "MetadataUnavailable" })
        };
    }

    [HttpDelete("credential")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var denied = await GuardAsync(ct);
        if (denied is not null) return denied;
        return Ok(await _integrations.DisconnectAsync(_tenantContext.TenantId, Provider, ct));
    }

    private async Task<IActionResult?> GuardAsync(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (await _productService.GetAsync(tenantId, ct) != ResolvedTenantProduct.Business)
            return StatusCode(StatusCodes.Status403Forbidden, "The Rivhit integration is not available for this tenant.");

        return null;
    }
}
