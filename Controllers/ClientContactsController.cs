using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;

namespace Clienta.Api.Controllers;

// PM (Project Management) foundation - Phase 1A.
// Isolated controller; does not modify ClientsController.
[ApiController]
[Route("api/client-contacts")]
[Authorize(Policy = "manage_projects")]
public class ClientContactsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IPmAccessService _pmAccessService;

    public ClientContactsController(AppDbContext context, ITenantContext tenantContext, IPmAccessService pmAccessService)
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

    private static ClientContactResponse ToResponse(ClientContact contact) => new(
        contact.Id,
        contact.ClientId,
        contact.Name,
        contact.RoleTitle,
        contact.Phone,
        contact.WhatsApp,
        contact.Email,
        contact.IsPrimary,
        contact.IsBillingContact,
        contact.CreatedAt,
        contact.UpdatedAt
    );

    // GET /api/clients/{clientId}/contacts
    [HttpGet("/api/clients/{clientId}/contacts")]
    public async Task<IActionResult> GetByClient(Guid clientId)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        var clientExists = await _context.Clients
            .AnyAsync(c => c.Id == clientId && c.TenantId == _tenantContext.TenantId);
        if (!clientExists)
            return NotFound("Client not found");

        var contacts = await _context.ClientContacts
            .Where(cc => cc.ClientId == clientId && cc.TenantId == _tenantContext.TenantId)
            .OrderByDescending(cc => cc.IsPrimary)
            .ThenBy(cc => cc.CreatedAt)
            .Select(cc => new ClientContactResponse(
                cc.Id, cc.ClientId, cc.Name, cc.RoleTitle, cc.Phone, cc.WhatsApp,
                cc.Email, cc.IsPrimary, cc.IsBillingContact, cc.CreatedAt, cc.UpdatedAt))
            .ToListAsync();

        return Ok(contacts);
    }

    // GET /api/client-contacts/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        var contact = await _context.ClientContacts
            .FirstOrDefaultAsync(cc => cc.Id == id && cc.TenantId == _tenantContext.TenantId);
        if (contact == null)
            return NotFound();

        return Ok(ToResponse(contact));
    }

    // POST /api/clients/{clientId}/contacts
    [HttpPost("/api/clients/{clientId}/contacts")]
    public async Task<IActionResult> Create(Guid clientId, [FromBody] CreateClientContactRequest request)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var clientExists = await _context.Clients
            .AnyAsync(c => c.Id == clientId && c.TenantId == _tenantContext.TenantId);
        if (!clientExists)
            return BadRequest("Client not found for the current tenant.");

        var contact = new ClientContact
        {
            ClientId = clientId,
            Name = request.Name.Trim(),
            RoleTitle = request.RoleTitle,
            Phone = request.Phone,
            WhatsApp = request.WhatsApp,
            Email = request.Email,
            IsPrimary = request.IsPrimary,
            IsBillingContact = request.IsBillingContact,
        };

        _context.ClientContacts.Add(contact);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = contact.Id }, ToResponse(contact));
    }

    // PUT /api/client-contacts/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClientContactRequest request)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var contact = await _context.ClientContacts
            .FirstOrDefaultAsync(cc => cc.Id == id && cc.TenantId == _tenantContext.TenantId);
        if (contact == null)
            return NotFound();

        contact.Name = request.Name.Trim();
        contact.RoleTitle = request.RoleTitle;
        contact.Phone = request.Phone;
        contact.WhatsApp = request.WhatsApp;
        contact.Email = request.Email;
        contact.IsPrimary = request.IsPrimary;
        contact.IsBillingContact = request.IsBillingContact;
        contact.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(ToResponse(contact));
    }

    // DELETE /api/client-contacts/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var gate = await EnsurePmEnabledAsync();
        if (gate != null) return gate;

        var contact = await _context.ClientContacts
            .FirstOrDefaultAsync(cc => cc.Id == id && cc.TenantId == _tenantContext.TenantId);
        if (contact == null)
            return NotFound();

        _context.ClientContacts.Remove(contact);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
