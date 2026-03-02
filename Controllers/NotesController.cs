using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Entities;
using Clienta.Api.DTOs;

namespace Clienta.Api.Controllers;

 [ApiController]
 [Authorize]
 [Route("api/notes")]
 public class NotesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IPlanEnforcementService _planEnforcement;

    public NotesController(
        AppDbContext context, 
        ITenantContext tenant,
        IPlanEnforcementService planEnforcement)
    {
        _context = context;
        _tenant = tenant;
        _planEnforcement = planEnforcement;
    }

    // POST /notes
    [HttpPost]
    public async Task<IActionResult> CreateNote([FromBody] CreateNoteRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (_tenant.UserId == null)
            return Unauthorized();

        var clientExists = await _context.Clients
            .AnyAsync(c => c.Id == request.ClientId);

        if (!clientExists)
            return BadRequest("Client does not exist.");

        var note = new Note
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ClientId = request.ClientId,
            CreatedByUserId = _tenant.UserId.Value,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notes.Add(note);
        await _context.SaveChangesAsync();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == note.CreatedByUserId);

        return Ok(new NoteResponse(
            note.Id,
            note.ClientId,
            note.Content,
            note.CreatedByUserId,
            user?.FullName ?? string.Empty,
            note.CreatedAt
        ));
    }

    // PUT /notes/{id}
    [Authorize(Policy = "manage_clients")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateNoteRequest request)
    {
        var note = await _context.Notes
            .FirstOrDefaultAsync(n => n.Id == id);

        if (note == null)
            return NotFound();

        // 🔐 Ownership rule: Staff can edit only their own notes
        // Admin can edit any note
        if (User.FindFirstValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role") != "Admin" && 
            note.CreatedByUserId != _tenant.UserId)
        {
            return Forbid();
        }

        note.Content = request.Content;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // DELETE /notes/{id}
    [Authorize(Policy = "manage_clients")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var note = await _context.Notes
            .FirstOrDefaultAsync(n => n.Id == id);

        if (note == null)
            return NotFound();

        // 🔐 Ownership rule: Staff can delete only their own notes
        // Admin can delete any note
        if (User.FindFirstValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role") != "Admin" &&
            note.CreatedByUserId != _tenant.UserId)
        {
            return Forbid();
        }

        _context.Notes.Remove(note);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // GET /clients/{clientId}/notes
    [Authorize(Policy = "manage_clients")]
    [HttpGet("~/api/clients/{clientId}/notes")]
    public async Task<IActionResult> GetByClient(Guid clientId)
    {
        var notes = await _context.Notes
            .Include(n => n.CreatedByUser)
            .Where(n => n.ClientId == clientId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NoteResponse(
                n.Id,
                n.ClientId,
                n.Content,
                n.CreatedByUserId,
                n.CreatedByUser.FullName,
                n.CreatedAt
            ))
            .ToListAsync();

        return Ok(notes);
    }

        // GET /notes?clientId={clientId}
        [HttpGet]
        public async Task<IActionResult> GetByClientId([FromQuery] Guid clientId)
        {
            var notes = await _context.Notes
                .Where(n => n.ClientId == clientId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            return Ok(notes);
        }
}
