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
    private const string DuplicateIdNumberCode = "DUPLICATE_CLIENT_ID_NUMBER";
    private const string DuplicateIdNumberMessage = "A client with this ID number already exists.";


    public ClientsController(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    private static string? NormalizeIdNumber(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber))
        {
            return null;
        }

        return idNumber.Trim();
    }

    // GET /api/clients
    [Authorize(Policy = "view_clients")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? searchTerm = null)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var clientsQuery = _context.Clients
            .Where(c => c.TenantId == _tenantContext.TenantId);

        var normalizedSearch = searchTerm?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            clientsQuery = clientsQuery.Where(c =>
                EF.Functions.ILike(c.FullName, $"%{normalizedSearch}%") ||
                (c.IdNumber != null && EF.Functions.ILike(c.IdNumber, $"%{normalizedSearch}%"))
            );
        }

        var clients = await clientsQuery
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

    [Authorize(Policy = "manage_clients")]
    [HttpGet("check-id-number")]
    public async Task<IActionResult> CheckDuplicateIdNumber([FromQuery] string? idNumber, [FromQuery] Guid? excludeClientId)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var normalizedIdNumber = NormalizeIdNumber(idNumber);
        if (normalizedIdNumber is null)
        {
            return Ok(new DuplicateClientIdNumberCheckResponse(false, null, null));
        }

        var duplicateClient = await _context.Clients
            .Where(c =>
                c.TenantId == _tenantContext.TenantId &&
                c.IdNumber != null &&
                c.IdNumber.Trim() == normalizedIdNumber &&
                (!excludeClientId.HasValue || c.Id != excludeClientId.Value))
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.Id, c.FullName })
            .FirstOrDefaultAsync();

        return Ok(new DuplicateClientIdNumberCheckResponse(
            duplicateClient != null,
            duplicateClient?.Id,
            duplicateClient?.FullName
        ));
    }

    // POST /api/clients
    [Authorize(Policy = "manage_clients")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateClientRequest request)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var normalizedIdNumber = NormalizeIdNumber(request.IdNumber);

        if (normalizedIdNumber is not null)
        {
            var duplicateClient = await _context.Clients
                .Where(c => c.TenantId == _tenantContext.TenantId && c.IdNumber != null && c.IdNumber.Trim() == normalizedIdNumber)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new { c.Id, c.FullName })
                .FirstOrDefaultAsync();

            if (duplicateClient != null)
            {
                return Conflict(new
                {
                    code = DuplicateIdNumberCode,
                    message = DuplicateIdNumberMessage,
                    clientId = duplicateClient.Id,
                    clientName = duplicateClient.FullName
                });
            }
        }

        var client = new Client
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            FullName = request.FullName,
            IdNumber = normalizedIdNumber,
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

        var normalizedIdNumber = NormalizeIdNumber(request.IdNumber);

        if (normalizedIdNumber is not null)
        {
            var duplicateClient = await _context.Clients
                .Where(c =>
                    c.TenantId == _tenantContext.TenantId &&
                    c.Id != id &&
                    c.IdNumber != null &&
                    c.IdNumber.Trim() == normalizedIdNumber)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new { c.Id, c.FullName })
                .FirstOrDefaultAsync();

            if (duplicateClient != null)
            {
                return Conflict(new
                {
                    code = DuplicateIdNumberCode,
                    message = DuplicateIdNumberMessage,
                    clientId = duplicateClient.Id,
                    clientName = duplicateClient.FullName
                });
            }
        }

        client.FullName = request.FullName;
        client.IdNumber = normalizedIdNumber;
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
