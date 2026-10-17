using Clienta.Api.Authorization;
using Clienta.Api.DTOs;
using Clienta.Api.Services;
using Clienta.Api.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/platform")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.PlatformScheme, Policy = PlatformAuthConstants.PolicyOwner)]
public sealed class PlatformAdminController : ControllerBase
{
    private readonly IPlatformUserManagementService _platformUserManagementService;
    private readonly IPlatformSettingsService _platformSettingsService;
    private readonly IBusinessCommercialPlanCatalog _businessCommercialPlanCatalog;

    public PlatformAdminController(
        IPlatformUserManagementService platformUserManagementService,
        IPlatformSettingsService platformSettingsService,
        IBusinessCommercialPlanCatalog businessCommercialPlanCatalog)
    {
        _platformUserManagementService = platformUserManagementService;
        _platformSettingsService = platformSettingsService;
        _businessCommercialPlanCatalog = businessCommercialPlanCatalog;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] PlatformUsersQuery query, CancellationToken cancellationToken)
        => Ok(await _platformUserManagementService.GetUsersAsync(query, cancellationToken));

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] PlatformUserCreateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var result = await _platformUserManagementService.CreateUserAsync(request, currentUserId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { code = "PLATFORM_USER_CONFLICT", message = exception.Message });
        }
    }

    [HttpPut("users/{userId:guid}")]
    public async Task<IActionResult> UpdateUser(Guid userId, [FromBody] PlatformUserUpdateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var result = await _platformUserManagementService.UpdateUserAsync(userId, request, currentUserId, cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { code = "PLATFORM_USER_CONFLICT", message = exception.Message });
        }
    }

    [HttpPost("users/{userId:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid userId, [FromBody] PlatformUserPasswordResetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _platformUserManagementService.ResetPasswordAsync(userId, request, GetCurrentUserId(), cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { code = "PLATFORM_USER_CONFLICT", message = exception.Message });
        }
    }

    [HttpPost("users/{userId:guid}/force-password-reset")]
    public async Task<IActionResult> ForcePasswordReset(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _platformUserManagementService.ForcePasswordResetAsync(userId, GetCurrentUserId(), cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { code = "PLATFORM_USER_CONFLICT", message = exception.Message });
        }
    }

    [HttpPost("users/{userId:guid}/enable")]
    public async Task<IActionResult> EnableUser(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _platformUserManagementService.SetEnabledAsync(userId, true, GetCurrentUserId(), cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { code = "PLATFORM_USER_CONFLICT", message = exception.Message });
        }
    }

    [HttpPost("users/{userId:guid}/disable")]
    public async Task<IActionResult> DisableUser(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _platformUserManagementService.SetEnabledAsync(userId, false, GetCurrentUserId(), cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { code = "PLATFORM_USER_CONFLICT", message = exception.Message });
        }
    }

    [HttpPost("users/{userId:guid}/change-role")]
    public async Task<IActionResult> ChangeRole(Guid userId, [FromBody] PlatformUserRoleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _platformUserManagementService.ChangeRoleAsync(userId, request, GetCurrentUserId(), cancellationToken);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { code = "PLATFORM_USER_CONFLICT", message = exception.Message });
        }
    }

    [HttpDelete("users/{userId:guid}")]
    public async Task<IActionResult> DeleteUser(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await _platformUserManagementService.DeleteUserAsync(userId, GetCurrentUserId(), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { code = "PLATFORM_USER_CONFLICT", message = exception.Message });
        }
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
        => Ok(await _platformSettingsService.GetAsync(cancellationToken));

    [AllowAnonymous]
    [HttpGet("settings/public")]
    public async Task<IActionResult> GetPublicSettings(CancellationToken cancellationToken)
        => Ok(await _platformSettingsService.GetPublicAsync(cancellationToken));

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdatePlatformSettingsRequest request, CancellationToken cancellationToken)
        => Ok(await _platformSettingsService.UpdateAsync(request, cancellationToken));

    [HttpGet("business-plans")]
    public async Task<IActionResult> GetBusinessPlans(CancellationToken cancellationToken)
    {
        var definitions = await _businessCommercialPlanCatalog.GetAllAsync(cancellationToken);
        return Ok(definitions.Select(BusinessCommercialPlanDtoMapper.ToDto));
    }

    [HttpPut("business-plans/{planType}")]
    public async Task<IActionResult> UpdateBusinessPlan(
        string planType,
        [FromBody] UpdateBusinessPlanConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Models.PlanType>(planType, true, out var parsedPlan) ||
            parsedPlan is not (Models.PlanType.Trial or Models.PlanType.Basic or Models.PlanType.Pro))
        {
            return BadRequest(new { message = "Only Business Trial, Basic, and Pro definitions can be configured." });
        }

        try
        {
            var updated = await _businessCommercialPlanCatalog.UpdateAsync(parsedPlan, request, cancellationToken);
            return updated is null ? NotFound() : Ok(BusinessCommercialPlanDtoMapper.ToDto(updated));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }
}
