using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Entities;
using Clienta.Api.DTOs;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/appointments")]
[Authorize(Policy = "manage_appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IPlanEnforcementService _planEnforcement;

    public AppointmentsController(
        AppDbContext context, 
        ITenantContext tenant,
        IPlanEnforcementService planEnforcement)
    {
        _context = context;
        _tenant = tenant;
        _planEnforcement = planEnforcement;
    }

    // GET /appointments
    [Authorize(Policy = "manage_appointments")]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var appointments = await _context.Appointments
            .Include(a => a.Client)
            .OrderByDescending(a => a.StartTime)
            .Select(a => new AppointmentResponse(
                a.Id,
                a.ClientId,
                a.Client.FullName,
                a.StartTime,
                a.EndTime,
                a.Status,
                a.Notes,
                a.CreatedAt
            ))
            .ToListAsync();

        return Ok(appointments);
    }

    // GET /appointments/{id}
    [Authorize(Policy = "manage_appointments")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Client)
            .Where(a => a.Id == id)
            .Select(a => new AppointmentResponse(
                a.Id,
                a.ClientId,
                a.Client.FullName,
                a.StartTime,
                a.EndTime,
                a.Status,
                a.Notes,
                a.CreatedAt
            ))
            .FirstOrDefaultAsync();

        if (appointment == null)
            return NotFound();

        return Ok(appointment);
    }

    // POST /appointments
    [Authorize(Policy = "manage_appointments")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentRequest request)
    {
        // Ensure client belongs to this Business (Tenant filter applies automatically)
        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == request.ClientId);

        if (client == null)
            return BadRequest("Invalid client.");

        if (request.EndTime <= request.StartTime)
            return BadRequest("End time must be after start time.");

        // Check appointment limit via central enforcement
        try
        {
            await _planEnforcement.EnsureMessageLimitAsync(_tenant.TenantId);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ClientId = request.ClientId,
            CreatedByUserId = _tenant.UserId!.Value,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = "Scheduled",
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById),
            new { id = appointment.Id },
            new AppointmentResponse(
                appointment.Id,
                appointment.ClientId,
                client.FullName,
                appointment.StartTime,
                appointment.EndTime,
                appointment.Status,
                appointment.Notes,
                appointment.CreatedAt
            ));
    }

    // PUT /appointments/{id}
    [Authorize(Policy = "manage_appointments")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateAppointmentRequest request)
    {
        var appointment = await _context.Appointments
            .FirstOrDefaultAsync(a => a.Id == id);

        if (appointment == null)
            return NotFound();

        if (request.EndTime <= request.StartTime)
            return BadRequest("End time must be after start time.");

        appointment.StartTime = request.StartTime;
        appointment.EndTime = request.EndTime;
        appointment.Status = request.Status;
        appointment.Notes = request.Notes;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // DELETE /appointments/{id}
    [Authorize(Policy = "manage_appointments")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var appointment = await _context.Appointments
            .FirstOrDefaultAsync(a => a.Id == id);

        if (appointment == null)
            return NotFound();

        _context.Appointments.Remove(appointment);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
