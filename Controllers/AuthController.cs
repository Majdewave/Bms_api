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
    private readonly IOnboardingLocalizationService _onboardingLocalization;
    private readonly ITenantApprovalService _tenantApprovalService;
    private readonly IConfiguration _configuration;

    public AuthController(
        AppDbContext context,
        JwtService jwtService,
        IAuthService authService,
        ILogger<AuthController> logger,
        ResetRateLimiter rateLimiter,
        IEmailService emailService,
        IOnboardingLocalizationService onboardingLocalization,
        ITenantApprovalService tenantApprovalService,
        IConfiguration configuration)
    {
        _context = context;
        _jwtService = jwtService;
        _authService = authService;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _emailService = emailService;
        _onboardingLocalization = onboardingLocalization;
        _tenantApprovalService = tenantApprovalService;
        _configuration = configuration;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        try
        {
            var language = _onboardingLocalization.ResolveLanguage(request.Language, Request.Headers["Accept-Language"].ToString());

            if (string.IsNullOrWhiteSpace(request.BusinessName))
            {
                return BadRequest(new
                {
                    code = "BUSINESS_NAME_REQUIRED",
                    message = _onboardingLocalization.GetMessage("BUSINESS_NAME_REQUIRED", language),
                    field = "businessName"
                });
            }

            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                return BadRequest(new
                {
                    code = "FULL_NAME_REQUIRED",
                    message = _onboardingLocalization.GetMessage("FULL_NAME_REQUIRED", language),
                    field = "fullName"
                });
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new
                {
                    code = "EMAIL_REQUIRED",
                    message = _onboardingLocalization.GetMessage("EMAIL_REQUIRED", language),
                    field = "email"
                });
            }

            if (string.IsNullOrWhiteSpace(request.Phone))
            {
                return BadRequest(new
                {
                    code = "PHONE_REQUIRED",
                    message = _onboardingLocalization.GetMessage("PHONE_REQUIRED", language),
                    field = "phone"
                });
            }

            var normalizedPhone = request.Phone.Trim();
            var phoneDigits = new string(normalizedPhone.Where(char.IsDigit).ToArray());
            if (phoneDigits.Length < 7 || phoneDigits.Length > 15 || !System.Text.RegularExpressions.Regex.IsMatch(normalizedPhone, @"^\+?[0-9()\-\s.]{7,20}$"))
            {
                return BadRequest(new
                {
                    code = "PHONE_INVALID",
                    message = _onboardingLocalization.GetMessage("PHONE_INVALID", language),
                    field = "phone"
                });
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new
                {
                    code = "PASSWORD_REQUIRED",
                    message = _onboardingLocalization.GetMessage("PASSWORD_REQUIRED", language),
                    field = "password"
                });
            }

            if (request.Password.Length < 6)
            {
                return BadRequest(new
                {
                    code = "PASSWORD_TOO_SHORT",
                    message = _onboardingLocalization.GetMessage("PASSWORD_TOO_SHORT", language),
                    field = "password"
                });
            }

            if (string.IsNullOrWhiteSpace(request.ConfirmPassword) || request.Password != request.ConfirmPassword)
            {
                return BadRequest(new
                {
                    code = "PASSWORDS_DO_NOT_MATCH",
                    message = _onboardingLocalization.GetMessage("PASSWORDS_DO_NOT_MATCH", language),
                    field = "confirmPassword"
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
                    message = _onboardingLocalization.GetMessage("USER_ALREADY_EXISTS", language),
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
                    message = _onboardingLocalization.GetMessage("BUSINESS_ALREADY_EXISTS", language),
                    field = "businessName"
                });
            }

            DateTime? registrationLocal = null;
            var registrationUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.TimeZoneId))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
                    registrationLocal = TimeZoneInfo.ConvertTimeFromUtc(registrationUtc, tz);
                }
                catch
                {
                    registrationLocal = null;
                }
            }


            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = request.BusinessName,
                Phone = normalizedPhone,
                Subdomain = subdomain,
                Plan = PlanType.Trial,
                SubscriptionStatus = SubscriptionStatus.PendingApproval,
                PreferredLanguage = language,
                TrialStartsAt = null,
                TrialEndsAt = null,
                IsSuspended = false,
                CreatedAt = registrationUtc,
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
                    "🎉 New Clienta Registration (Pending Approval)",
                    BuildRegistrationAdminEmail(tenant, user, request, language, registrationUtc, registrationLocal)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending admin registration notification");
            }


            try
            {
                var emailLanguage = _onboardingLocalization.ResolveLanguage(tenant.PreferredLanguage, Request.Headers["Accept-Language"].ToString());
                var customerWelcome = _onboardingLocalization.BuildPendingApprovalWelcomeEmail(emailLanguage, user.FullName ?? string.Empty);
                await _emailService.SendEmailAsync(
                    user.Email,
                    customerWelcome.Subject,
                    customerWelcome.HtmlBody
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending pending-approval welcome email");
            }

            return Ok(new
            {
                success = true,
                message = _onboardingLocalization.GetMessage("REGISTER_PENDING_APPROVAL_SUCCESS", language),
                status = "PendingApproval",
                tenantId = tenant.Id
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

        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == user.TenantId);

        if (tenant != null && tenant.IsSuspended)
        {
            var language = _onboardingLocalization.ResolveLanguage(tenant.PreferredLanguage, Request.Headers["Accept-Language"].ToString());
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "ACCOUNT_SUSPENDED",
                message = _onboardingLocalization.GetMessage("ACCOUNT_SUSPENDED", language)
            });
        }

        if (tenant != null && tenant.SubscriptionStatus == SubscriptionStatus.PendingApproval)
        {
            var language = _onboardingLocalization.ResolveLanguage(tenant.PreferredLanguage, Request.Headers["Accept-Language"].ToString());
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "ACCOUNT_PENDING_APPROVAL",
                message = _onboardingLocalization.GetMessage("PENDING_APPROVAL_LOGIN", language)
            });
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

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

    [AllowAnonymous]
    [HttpPost("approve-tenant/{tenantId:guid}")]
    public async Task<IActionResult> ApproveTenant(
        Guid tenantId,
        [FromHeader(Name = "X-Approval-Key")] string? approvalKey,
        CancellationToken cancellationToken)
    {
        var configuredApprovalKey = _configuration["Onboarding:ApprovalApiKey"];

        if (string.IsNullOrWhiteSpace(configuredApprovalKey))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "APPROVAL_NOT_CONFIGURED",
                message = "Approval endpoint is not configured."
            });
        }

        if (string.IsNullOrWhiteSpace(approvalKey) || approvalKey != configuredApprovalKey)
        {
            return Unauthorized(new
            {
                code = "INVALID_APPROVAL_KEY",
                message = "Invalid approval key."
            });
        }

        try
        {
            await _tenantApprovalService.ApproveTenantAsync(tenantId, cancellationToken);

            return Ok(new
            {
                success = true,
                status = SubscriptionStatus.Trialing.ToString(),
                tenantId
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                code = "APPROVAL_INVALID_STATE",
                message = ex.Message
            });
        }
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
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Token and password are required");
        }

        if (request.Password.Length < 8)
            return BadRequest("Password must be at least 8 characters");

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

    private static string BuildRegistrationAdminEmail(
        Tenant tenant,
        User user,
        RegisterRequest request,
        string preferredLanguage,
        DateTime registrationUtc,
        DateTime? registrationLocal)
    {
        var localSection = registrationLocal.HasValue
            ? $"<p><strong>Registration Date & Time (Local):</strong> {registrationLocal.Value:yyyy-MM-dd HH:mm:ss}</p>"
            : string.Empty;

        return $@"
            <h2>New Clienta Registration Received</h2>
            <p><strong>Business Name:</strong> {tenant.Name}</p>
            <p><strong>Owner:</strong> {user.FullName}</p>
            <p><strong>Phone:</strong> {(string.IsNullOrWhiteSpace(tenant.Phone) ? "N/A" : tenant.Phone)}</p>
            <p><strong>Email:</strong> {user.Email}</p>
            <p><strong>Preferred Language:</strong> {preferredLanguage}</p>
            <p><strong>Business Type:</strong> {(string.IsNullOrWhiteSpace(request.BusinessType) ? "N/A" : request.BusinessType)}</p>
            <p><strong>Registration Date & Time (UTC):</strong> {registrationUtc:yyyy-MM-dd HH:mm:ss} UTC</p>
            {localSection}
            <p><strong>Tenant Id:</strong> {tenant.Id}</p>
            <p><strong>Subdomain:</strong> {tenant.Subdomain}</p>
            <p><strong>Trial Plan:</strong> {tenant.Plan} (starts after approval)</p>
            <p><strong>Status:</strong> {tenant.SubscriptionStatus}</p>
        ";
    }
}

public record LoginRequest(string Email, string Password);

