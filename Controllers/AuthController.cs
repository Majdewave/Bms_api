using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Services;

using Clienta.Api.DTOs;


namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly JwtService _jwtService;
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext context,
        JwtService jwtService,
        IAuthService authService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Email == request.Email);

        if (user == null || !user.IsActive)
            return Unauthorized("Invalid credentials");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized("Invalid credentials");

        var token = _jwtService.GenerateToken(user, user.TenantId);

        var permissions = await _context.UserPermissions
            .Where(p => p.UserId == user.Id)
            .Select(p => p.Permission.Key)
            .ToListAsync();

        return Ok(new {
            token = token,
            user = new {
                id = user.Id,
                email = user.Email,
                name = user.FullName,
                role = user.Role,
                businessId = user.TenantId,
                permissions = permissions,
                stampUrl = user.StampUrl,
                useStamp = user.UseStamp
            }
        });
    }

    /// <summary>
    /// Get current authenticated user
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
            return Unauthorized();

        // Ignore query filters since we don't have tenant context yet
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userGuid);
        
        if (user == null || !user.IsActive)
            return Unauthorized();

        var permissions = await _context.UserPermissions
            .Where(up => up.UserId == user.Id)
            .Include(up => up.Permission)
            .Select(up => up.Permission.Key)
            .ToListAsync();

        return Ok(new
        {
            id = user.Id.ToString(),
            email = user.Email,
            name = user.FullName,
            role = user.Role.ToLower(),
            businessId = user.TenantId.ToString(),
            permissions = permissions,
            stampUrl = user.StampUrl,
            useStamp = user.UseStamp
        });
    }

    /// <summary>
    /// Invite a new user (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("invite")]
    public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("Email is required");

        // Get current tenant from context
        var tenant = HttpContext.Items["TenantContext"] as ITenantContext;
        if (tenant?.TenantId == Guid.Empty)
            return Unauthorized("No business context");

        if (tenant == null)
        {
            return BadRequest("Tenant not found");
        }
        var success = await _authService.InviteUserAsync(request.Email, tenant.TenantId);

        if (!success)
            return BadRequest("User already exists");

        return Ok(new { message = "Invitation sent successfully" });
    }

    /// <summary>
    /// Accept an invite and set password
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

        // Extract tenantId from request or use a default (needs to be added to request model)
        // For now, using Guid.Empty as placeholder - should be extracted from token or request
        var tenantId = Guid.Empty; // TODO: Extract from token subdomain or request

        var success = await _authService.AcceptInviteAsync(
            request.Token,
            request.Password,
            request.FullName);

        if (!success)
            return BadRequest("Invalid or expired token");

        return Ok(new { message = "Account activated successfully" });
    }

    /// <summary>
    /// Request password reset (send reset email)
    /// </summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] RequestPasswordResetRequest request)
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
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest("Token and new password are required");

        if (request.NewPassword.Length < 8)
            return BadRequest("Password must be at least 8 characters");

        // Extract tenantId from request or use a default (needs to be added to request model)
        var tenantId = Guid.Empty; // TODO: Extract from token subdomain or request

        var success = await _authService.ResetPasswordAsync(
            request.Token,
            request.NewPassword);

        if (!success)
            return BadRequest("Invalid or expired token");

        return Ok(new { message = "Password reset successfully" });
    }

    /// <summary>
    /// Debug endpoint to view all claims in the current JWT token
    /// </summary>
    [Authorize]
    [HttpGet("debug/claims")]
    public IActionResult GetClaims()
    {
        var claims = User.Claims.Select(c => new
        {
            Type = c.Type,
            Value = c.Value
        }).ToList();

        return Ok(new
        {
            isAuthenticated = User.Identity?.IsAuthenticated,
            claims = claims
        });
    }
}

public record LoginRequest(string Email, string Password);

