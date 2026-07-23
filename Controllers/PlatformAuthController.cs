using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/platform/auth")]
public class PlatformAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PlatformJwtService _platformJwtService;

    public PlatformAuthController(AppDbContext db, PlatformJwtService platformJwtService)
    {
        _db = db;
        _platformJwtService = platformJwtService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] PlatformLoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new
            {
                code = "INVALID_PLATFORM_CREDENTIALS",
                message = "Email and password are required"
            });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user == null || !user.IsActive)
        {
            return Unauthorized(new
            {
                code = "INVALID_PLATFORM_CREDENTIALS",
                message = "Email or password is incorrect"
            });
        }

        var validPassword = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!validPassword)
        {
            return Unauthorized(new
            {
                code = "INVALID_PLATFORM_CREDENTIALS",
                message = "Email or password is incorrect"
            });
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var token = _platformJwtService.GenerateToken(user);

        return Ok(new
        {
            token,
            user = new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                role = user.Role.ToString(),
                isActive = user.IsActive,
                lastLoginAt = user.LastLoginAt,
                createdAt = user.CreatedAt
            }
        });
    }

    [Authorize(AuthenticationSchemes = PlatformAuthConstants.PlatformScheme, Policy = PlatformAuthConstants.PolicySupportOrAbove)]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(idClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _db.PlatformUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);

        if (user == null)
        {
            return Unauthorized();
        }

        return Ok(new
        {
            id = user.Id,
            fullName = user.FullName,
            email = user.Email,
            role = user.Role.ToString(),
            isActive = user.IsActive,
            lastLoginAt = user.LastLoginAt,
            createdAt = user.CreatedAt,
            updatedAt = user.UpdatedAt
        });
    }
}
