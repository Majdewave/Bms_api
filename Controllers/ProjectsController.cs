using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;

namespace Clienta.Api.Controllers;

// PM (Project Management) foundation - Phase 1A.
// Isolated controller; does not modify ClientsController/QuotesController/InvoicesController.
[ApiController]
[Route("api/projects")]
[Authorize(Policy = "manage_projects")]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IPmAccessService _pmAccessService;

    public ProjectsController(AppDbContext context, ITenantContext tenantContext, IPmAccessService pmAccessService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _pmAccessService = pmAccessService;
    }

    private async Task<IActionResult?> EnsurePmEnabledAsync()
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await _pmAccessService.IsPmEnabledAsync(_tenantContext.TenantId))
            return StatusCode(StatusCodes.Status403Forbidden, "PM feature is not enabled for this tenant.");

        return null;
    }

    // Project StartDate/EndDate are calendar dates with no time-of-day/timezone meaning.
    // Npgsql requires DateTimeKind.Utc for "timestamp with time zone" columns, so normalize here
    // (same convention already used for Client.BirthDate) instead of changing the column type.
    private static DateTime? NormalizeToUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private static ProjectResponse ToResponse(Project project, string clientName) => new(
        project.Id,
        project.ClientId,
        clientName,
        project.Name,
        project.Status,
        project.Value,
        project.StartDate,
        project.EndDate,
        project.Description,
        project.CreatedAt,
        project.UpdatedAt
    );

    // GET /api/projects
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        var projects = await _context.Projects
            .Where(p => p.TenantId == _tenantContext.TenantId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectResponse(
                p.Id, p.ClientId, p.Client.FullName, p.Name, p.Status, p.Value,
                p.StartDate, p.EndDate, p.Description, p.CreatedAt, p.UpdatedAt))
            .ToListAsync();

        return Ok(projects);
    }

    // GET /api/projects/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        var project = await _context.Projects
            .Where(p => p.Id == id && p.TenantId == _tenantContext.TenantId)
            .Select(p => new ProjectResponse(
                p.Id, p.ClientId, p.Client.FullName, p.Name, p.Status, p.Value,
                p.StartDate, p.EndDate, p.Description, p.CreatedAt, p.UpdatedAt))
            .FirstOrDefaultAsync();

        if (project == null)
            return NotFound();

        return Ok(project);
    }

    // GET /api/clients/{clientId}/projects
    [HttpGet("/api/clients/{clientId}/projects")]
    public async Task<IActionResult> GetByClient(Guid clientId)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        var clientExists = await _context.Clients
            .AnyAsync(c => c.Id == clientId && c.TenantId == _tenantContext.TenantId);
        if (!clientExists)
            return NotFound("Client not found");

        var projects = await _context.Projects
            .Where(p => p.ClientId == clientId && p.TenantId == _tenantContext.TenantId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectResponse(
                p.Id, p.ClientId, p.Client.FullName, p.Name, p.Status, p.Value,
                p.StartDate, p.EndDate, p.Description, p.CreatedAt, p.UpdatedAt))
            .ToListAsync();

        return Ok(projects);
    }

    // POST /api/projects
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest request)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == request.ClientId && c.TenantId == _tenantContext.TenantId);
        if (client == null)
            return BadRequest("Client not found for the current tenant.");

        var project = new Project
        {
            ClientId = request.ClientId,
            Name = request.Name.Trim(),
            Status = request.Status,
            Value = request.Value,
            StartDate = NormalizeToUtc(request.StartDate),
            EndDate = NormalizeToUtc(request.EndDate),
            Description = request.Description,
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = project.Id }, ToResponse(project, client.FullName));
    }

    // PUT /api/projects/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProjectRequest request)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var project = await _context.Projects
            .Include(p => p.Client)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId);
        if (project == null)
            return NotFound();

        project.Name = request.Name.Trim();
        project.Status = request.Status;
        project.Value = request.Value;
        project.StartDate = NormalizeToUtc(request.StartDate);
        project.EndDate = NormalizeToUtc(request.EndDate);
        project.Description = request.Description;
        project.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(ToResponse(project, project.Client.FullName));
    }

    // DELETE /api/projects/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId);
        if (project == null)
            return NotFound();

        if (await _context.ClientFiles.AnyAsync(f => f.ProjectId == id && f.TenantId == _tenantContext.TenantId))
            return Conflict("This project has documents and cannot be deleted. Delete its documents first.");

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
