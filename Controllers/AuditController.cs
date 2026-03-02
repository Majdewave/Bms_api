using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;

namespace Clienta.Api.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _context;

    public AuditController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get last 100 audit logs (Admin only)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var logs = await _context.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// Get audit logs for a specific entity (Admin only)
    /// </summary>
    [HttpGet("entity/{entityName}")]
    public async Task<IActionResult> GetByEntity(string entityName)
    {
        var logs = await _context.AuditLogs
            .Where(a => a.EntityName == entityName)
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// Get audit logs for a specific record (Admin only)
    /// </summary>
    [HttpGet("entity/{entityName}/{entityId}")]
    public async Task<IActionResult> GetByEntityAndId(string entityName, string entityId)
    {
        var logs = await _context.AuditLogs
            .Where(a => a.EntityName == entityName && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// Get audit logs by user (Admin only)
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetByUser(Guid userId)
    {
        var logs = await _context.AuditLogs
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync();

        return Ok(logs);
    }
}
