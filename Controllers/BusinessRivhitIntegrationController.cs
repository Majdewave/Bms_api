using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Clienta.Api.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

// Rivhit configuration and accounting are available to Business, Clinic, and Legacy tenants; payment metadata remains Business-only.
[ApiController]
[Route("api/business/integrations/rivhit")]
[Authorize(Policy = IntegrationAuthorization.ManagePolicy)]
public class BusinessRivhitIntegrationController : ControllerBase
{
    private const IntegrationProvider Provider = IntegrationProvider.Rivhit;

    private readonly ITenantContext _tenantContext;
    private readonly ITenantProductService _productService;
    private readonly ITenantIntegrationService _integrations;
    private readonly AppDbContext _db;

    public BusinessRivhitIntegrationController(
        ITenantContext tenantContext,
        ITenantProductService productService,
        ITenantIntegrationService integrations,
        AppDbContext db)
    {
        _tenantContext = tenantContext;
        _productService = productService;
        _integrations = integrations;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var denied = await GuardAsync(ct, allowClinicConfiguration: true);
        if (denied is not null) return denied;
        return Ok(await _integrations.GetStateAsync(_tenantContext.TenantId, Provider, ct));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateIntegrationRequest request, CancellationToken ct)
    {
        var denied = await GuardAsync(ct, allowClinicConfiguration: true);
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
        var denied = await GuardAsync(ct, allowClinicConfiguration: true);
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
        var denied = await GuardAsync(ct, allowClinicConfiguration: true);
        if (denied is not null) return denied;
        return Ok(await _integrations.DisconnectAsync(_tenantContext.TenantId, Provider, ct));
    }

    private async Task<IActionResult?> GuardAsync(CancellationToken ct, bool allowClinicConfiguration = false)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");
        if (!await _db.Tenants.IgnoreQueryFilters().AnyAsync(tenant => tenant.Id == tenantId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, "The Rivhit integration is not available for this tenant.");

        var product = await _productService.GetAsync(tenantId, ct);
        if (product != ResolvedTenantProduct.Business
            && !(allowClinicConfiguration && product is (ResolvedTenantProduct.Clinic or ResolvedTenantProduct.Legacy)))
            return StatusCode(StatusCodes.Status403Forbidden, "The Rivhit integration is not available for this tenant.");

        return null;
    }
}
