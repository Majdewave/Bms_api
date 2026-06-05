using Microsoft.Extensions.Caching.Memory;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly JwtService _jwtService;
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly ResetRateLimiter _rateLimiter;
    private readonly IEmailService _emailService;

    public AuthController(
        AppDbContext context,
        JwtService jwtService,
        IAuthService authService,
        ILogger<AuthController> logger,
        ResetRateLimiter rateLimiter,
        IEmailService emailService)
    {
        _context = context;
        _jwtService = jwtService;
        _authService = authService;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _emailService = emailService;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                return BadRequest(new
                {
                    code = "FULL_NAME_REQUIRED",
                    message = "Full name is required",
                    field = "fullName"
                });
            }
            var normalizedEmail = request.Email.Trim().ToLower();

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (existingUser != null)
            {
                return BadRequest(new
                {
                    code = "USER_ALREADY_EXISTS",
                    message = "User with this email already exists",
                    field = "email"
                });
            }
            var subdomain = request.BusinessName
            .Trim()
            .ToLower()
            .Replace(" ", "");

            var existingTenant = await _context.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Subdomain == subdomain);

            if (existingTenant != null)
            {
                return BadRequest(new
                {
                    code = "BUSINESS_ALREADY_EXISTS",
                    message = "Business name already taken",
                    field = "businessName"
                });
            }


            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = request.BusinessName,
                Subdomain = subdomain,
                Plan = PlanType.Trial,
                SubscriptionStatus = SubscriptionStatus.Trialing,
                TrialEndsAt = DateTime.UtcNow.AddDays(7),
                IsSuspended = false,
                CreatedAt = DateTime.UtcNow,
            };

            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();


            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                PasswordHash = HashPassword(request.Password),
                Role = "Admin",
                TenantId = tenant.Id,
                CreatedAt = DateTime.UtcNow,
                FullName = request.FullName.Trim()
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();


            await _context.Tenants
                .Where(t => t.Id == tenant.Id)
                .ExecuteUpdateAsync(t => t
                    .SetProperty(x => x.OwnerUserId, user.Id));



            // sending mail for me about every new Tenant (Trial)
            try
            {
                await _emailService.SendEmailAsync(
                    "mjd.salman@gmail.com",
                    "🎉 New Clienta Trial Registration",
                    $@"
                    <h2>New Trial Registration</h2>

                    <p><strong>Business:</strong> {tenant.Name}</p>
                    <p><strong>Email:</strong> {user.Email}</p>
                    <p><strong>Full Name:</strong> {user.FullName}</p>
                    <p><strong>Tenant Id:</strong> {tenant.Id}</p>
                     "
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending admin registration notification");
            }


            var token = _jwtService.GenerateToken(user, tenant.Id);

            // Return user object in the same format as Login
            var permissions = await _context.UserPermissions
                .Where(p => p.UserId == user.Id)
                .Select(p => p.Permission.Key)
                .ToListAsync();

            return Ok(new
            {
                token = token,
                user = new
                {
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
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new
            {
                code = "DATABASE_ERROR",
                message = ex.InnerException?.Message ?? "Database error"
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new
            {
                code = "UNKNOWN_ERROR",
                message = "Something went wrong"
            });
        }
    }


    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLower();

        var user = await _context.Users
            .IgnoreQueryFilters()
            .Where(x => x.Email.ToLower() == normalizedEmail)
            .OrderByDescending(x => x.FullName != null) // עדיף מי שיש לו שם
            .FirstOrDefaultAsync();

        if (user == null || !user.IsActive)
            return Unauthorized(new
            {
                code = "INVALID_CREDENTIALS",
                message = "Email or password is incorrect"
            });

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new
            {
                code = "INVALID_CREDENTIALS",
                message = "Email or password is incorrect"
            });

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

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // ✔ Rate limit
        if (!_rateLimiter.CanRequest(request.Email, ip))
        {
            return Ok(new
            {
                message = "If an account exists, a password reset email will be sent"
            });
        }

        await _authService.RequestPasswordResetAsync(request.Email);

        return Ok(new
        {
            message = "If an account exists, a password reset email will be sent"
        });
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

    private string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }
}

public record LoginRequest(string Email, string Password);

