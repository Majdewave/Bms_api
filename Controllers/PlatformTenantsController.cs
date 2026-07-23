using Clienta.Api.Authorization;
using Clienta.Api.DTOs;
using Clienta.Api.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/platform/tenants")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.PlatformScheme, Policy = PlatformAuthConstants.PolicyOwner)]
public class PlatformTenantsController : ControllerBase
{
    private readonly IPlatformTenantManagementService _tenantManagementService;

    public PlatformTenantsController(IPlatformTenantManagementService tenantManagementService)
    {
        _tenantManagementService = tenantManagementService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PlatformTenantsListResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenants([FromQuery] PlatformTenantsQuery query, CancellationToken cancellationToken)
    {
        var response = await _tenantManagementService.GetTenantsAsync(query, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{tenantId:guid}")]
    [ProducesResponseType(typeof(PlatformTenantDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantDetails([FromRoute] Guid tenantId, CancellationToken cancellationToken)
    {
        var details = await _tenantManagementService.GetTenantDetailsAsync(tenantId, cancellationToken);
        if (details == null)
        {
            return NotFound(new ApiErrorResponse("TENANT_NOT_FOUND", "Tenant was not found."));
        }

        return Ok(details);
    }

    [HttpGet("{tenantId:guid}/users")]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformTenantUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantUsers([FromRoute] Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var users = await _tenantManagementService.GetTenantUsersAsync(tenantId, cancellationToken);
            return Ok(users);
        }
        catch (PlatformTenantNotFoundException)
        {
            return NotFound(new ApiErrorResponse("TENANT_NOT_FOUND", "Tenant was not found."));
        }
    }

    [HttpGet("{tenantId:guid}/services")]
    [ProducesResponseType(typeof(PlatformTenantServicesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantServices([FromRoute] Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var services = await _tenantManagementService.GetTenantServicesAsync(tenantId, cancellationToken);
            return Ok(services);
        }
        catch (PlatformTenantNotFoundException)
        {
            return NotFound(new ApiErrorResponse("TENANT_NOT_FOUND", "Tenant was not found."));
        }
    }

    [HttpGet("{tenantId:guid}/activity")]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformTenantActivityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantActivity([FromRoute] Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var activity = await _tenantManagementService.GetTenantActivityAsync(tenantId, cancellationToken);
            return Ok(activity);
        }
        catch (PlatformTenantNotFoundException)
        {
            return NotFound(new ApiErrorResponse("TENANT_NOT_FOUND", "Tenant was not found."));
        }
    }

    [HttpPost("{tenantId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve([FromRoute] Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _tenantManagementService.ApproveTenantAsync(tenantId, cancellationToken);
            return NoContent();
        }
        catch (PlatformTenantNotFoundException)
        {
            return NotFound(new ApiErrorResponse("TENANT_NOT_FOUND", "Tenant was not found."));
        }
        catch (PlatformTenantConflictException ex)
        {
            return Conflict(new ApiErrorResponse("TENANT_STATE_CONFLICT", ex.Message));
        }
    }

    [HttpPost("{tenantId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject([FromRoute] Guid tenantId, [FromBody] RejectTenantRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _tenantManagementService.RejectTenantAsync(tenantId, request.Reason, cancellationToken);
            return NoContent();
        }
        catch (PlatformTenantNotFoundException)
        {
            return NotFound(new ApiErrorResponse("TENANT_NOT_FOUND", "Tenant was not found."));
        }
        catch (PlatformTenantConflictException ex)
        {
            return Conflict(new ApiErrorResponse("TENANT_STATE_CONFLICT", ex.Message));
        }
    }

    [HttpPost("{tenantId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Suspend([FromRoute] Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _tenantManagementService.SuspendTenantAsync(tenantId, cancellationToken);
            return NoContent();
        }
        catch (PlatformTenantNotFoundException)
        {
            return NotFound(new ApiErrorResponse("TENANT_NOT_FOUND", "Tenant was not found."));
        }
        catch (PlatformTenantConflictException ex)
        {
            return Conflict(new ApiErrorResponse("TENANT_STATE_CONFLICT", ex.Message));
        }
    }

    [HttpPost("{tenantId:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Activate([FromRoute] Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _tenantManagementService.ActivateTenantAsync(tenantId, cancellationToken);
            return NoContent();
        }
        catch (PlatformTenantNotFoundException)
        {
            return NotFound(new ApiErrorResponse("TENANT_NOT_FOUND", "Tenant was not found."));
        }
        catch (PlatformTenantConflictException ex)
        {
            return Conflict(new ApiErrorResponse("TENANT_STATE_CONFLICT", ex.Message));
        }
    }

    [HttpPost("{tenantId:guid}/extend-trial")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ExtendTrial([FromRoute] Guid tenantId, [FromBody] ExtendTrialRequest request, CancellationToken cancellationToken)
    {
        if (request.Days <= 0)
        {
            return BadRequest(new ApiErrorResponse("INVALID_TRIAL_EXTENSION", "Days must be greater than zero."));
        }

        try
        {
            await _tenantManagementService.ExtendTrialAsync(tenantId, request.Days, cancellationToken);
            return NoContent();
        }
        catch (PlatformTenantNotFoundException)
        {
            return NotFound(new ApiErrorResponse("TENANT_NOT_FOUND", "Tenant was not found."));
        }
        catch (PlatformTenantValidationException ex)
        {
            return BadRequest(new ApiErrorResponse("INVALID_TRIAL_EXTENSION", ex.Message));
        }
        catch (PlatformTenantConflictException ex)
        {
            return Conflict(new ApiErrorResponse("TENANT_STATE_CONFLICT", ex.Message));
        }
    }
}
