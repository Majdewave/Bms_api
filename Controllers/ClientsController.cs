using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.DTOs;
using Clienta.Api.Services;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/clients")]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;


    public ClientsController(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    // GET /api/clients
    [Authorize(Policy = "view_clients")]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var clients = await _context.Clients
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ClientResponse(
            c.Id,
            c.FullName,
            c.IdNumber,
            c.BirthDate,
            c.Email,
            c.Phone,
            c.Address,
            c.InternalNote,
            c.IsActive,
            c.CreatedAt,
            c.Status,
            _context.Appointments
                .Where(a => a.ClientId == c.Id && a.Status == AppointmentStatuses.Completed)
                .OrderByDescending(a => a.StartTime)
                .Select(a => (DateTime?)a.StartTime)
                .FirstOrDefault(),
            _context.Appointments
                .Any(a => a.ClientId == c.Id && !a.IsDocumented)
        ))
            .ToListAsync();
        return Ok(clients);
    }

    // GET /api/clients/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var client = await _context.Clients
            .Where(c => c.Id == id)
            .Select(c => new ClientResponse(
                c.Id,
                c.FullName,
                c.IdNumber,
                c.BirthDate,
                c.Email,
                c.Phone,
                c.Address,
                c.InternalNote,
                c.IsActive,
                c.CreatedAt,
                c.Status,
                _context.Appointments
                    .Where(a => a.ClientId == c.Id && a.Status == AppointmentStatuses.Completed)
                    .OrderByDescending(a => a.StartTime)
                    .Select(a => (DateTime?)a.StartTime)
                    .FirstOrDefault(),
                _context.Appointments
                    .Any(a => a.ClientId == c.Id && !a.IsDocumented)
            ))
            .FirstOrDefaultAsync();
        if (client == null)
            return NotFound();
        return Ok(client);
    }

    // POST /api/clients
    [Authorize(Policy = "manage_clients")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateClientRequest request)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var client = new Client
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            FullName = request.FullName,
            IdNumber = request.IdNumber,
            BirthDate = request.BirthDate.HasValue
                        ? DateTime.SpecifyKind(request.BirthDate.Value, DateTimeKind.Utc)
                        : null,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            InternalNote = request.InternalNote,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Status = "Active"
        };
        _context.Clients.Add(client);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = client.Id }, new ClientResponse(
            client.Id,
            client.FullName,
            client.IdNumber,
            client.BirthDate,
            client.Email,
            client.Phone,
            client.Address,
            client.InternalNote,
            client.IsActive,
            client.CreatedAt,
            client.Status,
            null, // LastVisit not present
            client.IsNotDocumented
        ));
    }

    // PUT /api/clients/{id}
    [Authorize(Policy = "manage_clients")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateClientRequest request)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client == null)
            return NotFound();
        client.FullName = request.FullName;
        client.IdNumber = request.IdNumber;
        client.BirthDate = request.BirthDate.HasValue
                            ? DateTime.SpecifyKind(request.BirthDate.Value, DateTimeKind.Utc)
                            : null;
        client.Email = request.Email;
        client.Phone = request.Phone;
        client.Address = request.Address;
        client.InternalNote = request.InternalNote;
        client.IsActive = request.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE /api/clients/{id}
    [Authorize(Policy = "manage_clients")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client == null)
            return NotFound();

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        User? currentUser = null;
        if (Guid.TryParse(currentUserId, out var parsedId))
        {
            currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == parsedId);
        }

        var performedByLabel = currentUser != null
            ? $"{currentUser.Role} - {currentUser.FullName}"
            : "Admin";

        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            UserId = currentUser?.Id,
            EntityName = "Client",
            ActionType = "client_deleted",
            EntityId = client.Id.ToString(),
            NewValues = client.FullName,
            PerformedBy = performedByLabel,
            CreatedAt = DateTime.UtcNow
        });

        _context.Clients.Remove(client);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
