using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/platform")]
[Authorize(AuthenticationSchemes = PlatformAuthConstants.PlatformScheme, Policy = PlatformAuthConstants.PolicyOwner)]
public class PlatformController : ControllerBase
{
    private readonly MasterDbContext _masterDb;
    private readonly IPlatformDashboardService _platformDashboardService;

    public PlatformController(
        MasterDbContext masterDb,
        IPlatformDashboardService platformDashboardService)
    {
        _masterDb = masterDb;
        _platformDashboardService = platformDashboardService;
    }

    [AllowAnonymous]
    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] PlatformBootstrapRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { code = "FULL_NAME_REQUIRED", message = "FullName is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { code = "EMAIL_REQUIRED", message = "Email is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { code = "PASSWORD_REQUIRED", message = "Password is required." });
        }

        var strategy = _masterDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = await _masterDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            if (await _masterDb.PlatformUsers.AnyAsync(cancellationToken))
            {
                return Conflict(new
                {
                    code = "PLATFORM_OWNER_ALREADY_CREATED",
                    message = "The Platform Owner has already been created."
                });
            }

            var now = DateTime.UtcNow;
            var owner = new PlatformUser
            {
                Id = Guid.NewGuid(),
                FullName = request.FullName.Trim(),
                Email = request.Email.Trim().ToLowerInvariant(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = PlatformRole.Owner,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            _masterDb.PlatformUsers.Add(owner);

            try
            {
                await _masterDb.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new
                {
                    code = "PLATFORM_OWNER_ALREADY_CREATED",
                    message = "The Platform Owner has already been created."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Platform Owner created successfully.",
                user = new
                {
                    owner.Id,
                    owner.FullName,
                    owner.Email,
                    role = owner.Role.ToString(),
                    owner.IsActive,
                    owner.CreatedAt,
                    owner.UpdatedAt
                }
            });
        });
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(PlatformDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var result = await _platformDashboardService.GetDashboardAsync(cancellationToken);
        return Ok(result);
    }

}
