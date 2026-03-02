using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Clienta.Api.Services;
using Clienta.Api.DTOs;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly IAuthService _authService;

    public AccountController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Invite a new user (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] InviteUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("Email is required");

        // Get current tenant from context
        var tenant = HttpContext.Items["TenantContext"] as ITenantContext;
        if (tenant?.TenantId == Guid.Empty)
            return Unauthorized("No business context");

        var success = await _authService.InviteUserAsync(request.Email, tenant.TenantId);

        if (!success)
            return BadRequest("User already exists");

        return Ok(new { message = "Invitation sent successfully" });
    }

    /// <summary>
    /// Accept an invite and create account
    /// </summary>
    [AllowAnonymous]
    [HttpPost("accept-invite")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest("Token, password, and full name are required");

        if (request.Password.Length < 8)
            return BadRequest("Password must be at least 8 characters");

        // Extract tenantId from request or token subdomain
        var tenantId = Guid.Empty; // TODO: Extract from token subdomain or request

        var success = await _authService.AcceptInviteAsync(
            request.Token,
            request.Password,
            request.FullName,
            tenantId);

        if (!success)
            return BadRequest("Invalid or expired token");

        return Ok(new { message = "Account activated successfully" });
    }

    /// <summary>
    /// Request password reset (send email)
    /// </summary>
    [AllowAnonymous]
    [HttpPost("request-reset")]
    public async Task<IActionResult> RequestReset([FromBody] RequestPasswordResetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("Email is required");

        // Always return success to prevent account enumeration
        await _authService.RequestPasswordResetAsync(request.Email);

        return Ok(new { message = "If an account exists, a password reset email will be sent" });
    }

    /// <summary>
    /// Confirm password reset with token
    /// </summary>
    [AllowAnonymous]
    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest("Token and new password are required");

        if (request.NewPassword.Length < 8)
            return BadRequest("Password must be at least 8 characters");

        // Extract tenantId from request or token subdomain
        var tenantId = Guid.Empty; // TODO: Extract from token subdomain or request

        var success = await _authService.ResetPasswordAsync(
            request.Token,
            request.NewPassword,
            tenantId);

        if (!success)
            return BadRequest("Invalid or expired token");

        return Ok(new { message = "Password reset successfully" });
    }
}
