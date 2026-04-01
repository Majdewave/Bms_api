using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Entities;
using Clienta.Api.DTOs;
using Microsoft.AspNetCore.SignalR;
using Clienta.Api.Hubs;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/appointments")]
[Authorize(Policy = "manage_appointments")]
public class AppointmentsController : ControllerBase
{
    // (Fields already declared above)

    // GET /appointments/queue
    [Authorize(Policy = "manage_appointments")]
    [HttpGet("queue")]
    public async Task<IActionResult> GetQueue()
    {
        var tenantId = _tenant.TenantId;
        var inProgress = await _context.Appointments
            .Include(a => a.Client)
            .Include(a => a.Service)
            .Include(a => a.Staff)
            .Where(a => a.TenantId == tenantId && a.Status == AppointmentStatuses.InProgress)
            .OrderBy(a => a.StartTime)
            .FirstOrDefaultAsync();

        var nextWaiting = await _context.Appointments
            .Include(a => a.Client)
            .Include(a => a.Service)
            .Include(a => a.Staff)
            .Where(a => a.TenantId == tenantId && a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.StartTime)
            .FirstOrDefaultAsync();

        var waitingCount = await _context.Appointments
            .CountAsync(a => a.TenantId == tenantId && a.Status == AppointmentStatuses.Waiting);

        return Ok(new
        {
            current = inProgress == null ? null : new AppointmentDto(
                inProgress.Id,
                inProgress.ClientId,
                inProgress.Client.FullName,
                inProgress.ServiceId,
                inProgress.Service != null ? inProgress.Service.Name : null,
                inProgress.StaffId,
                inProgress.Staff != null ? inProgress.Staff.User.FullName : null,
                inProgress.StartTime,
                inProgress.EndTime,
                inProgress.Status,
                inProgress.Notes,
                inProgress.CreatedAt,
                inProgress.IsDocumented ?? false
            ),
            next = nextWaiting == null ? null : new AppointmentDto(
                nextWaiting.Id,
                nextWaiting.ClientId,
                nextWaiting.Client.FullName,
                nextWaiting.ServiceId,
                nextWaiting.Service != null ? nextWaiting.Service.Name : null,
                nextWaiting.StaffId,
                nextWaiting.Staff != null ? nextWaiting.Staff.User.FullName : null,
                nextWaiting.StartTime,
                nextWaiting.EndTime,
                nextWaiting.Status,
                nextWaiting.Notes,
                nextWaiting.CreatedAt,
                nextWaiting.IsDocumented ?? false
            ),
            waitingCount
        });
    }
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IPlanEnforcementService _planEnforcement;
    private readonly IHubContext<AppointmentsHub> _hubContext;

    public AppointmentsController(
        AppDbContext context,
        ITenantContext tenant,
        IPlanEnforcementService planEnforcement,
        IHubContext<AppointmentsHub> hubContext)
    {
        _context = context;
        _tenant = tenant;
        _planEnforcement = planEnforcement;
        _hubContext = hubContext;
    }

    // GET /appointments
    [Authorize(Policy = "manage_appointments")]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var appointments = await _context.Appointments
            .Include(a => a.Client)
            .Include(a => a.Service)
            .Include(a => a.Staff)
            .OrderByDescending(a => a.StartTime)
            .Select(a => new AppointmentDto(
                a.Id,
                a.ClientId,
                a.Client.FullName,
                a.ServiceId,
                a.Service != null ? a.Service.Name : null,
                a.StaffId,
                a.Staff != null ? a.Staff.User.FullName : null,
                a.StartTime,
                a.EndTime,
                a.Status,
                a.Notes,
                a.CreatedAt,
                a.IsDocumented ?? false
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
            .Include(a => a.Service)
            .Include(a => a.Staff)
            .Where(a => a.Id == id)
            .Select(a => new AppointmentDto(
                a.Id,
                a.ClientId,
                a.Client.FullName,
                a.ServiceId,
                a.Service != null ? a.Service.Name : null,
                a.StaffId,
                a.Staff != null ? a.Staff.User.FullName : null,
                a.StartTime,
                a.EndTime,
                a.Status,
                a.Notes,
                a.CreatedAt,
                a.IsDocumented ?? false
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
        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == request.ClientId);

        if (client == null)
            return BadRequest("Invalid client.");

        if (request.StartTime != default && request.EndTime != default &&
            request.EndTime <= request.StartTime)
        {
            return BadRequest("End time must be after start time.");
        }

        // Validate ServiceId if provided
        if (request.ServiceId.HasValue)
        {
            var serviceExists = await _context.Services
                .AnyAsync(s => s.Id == request.ServiceId);

            if (!serviceExists)
                return BadRequest("Invalid service.");
        }

        // Validate StaffId if provided
        if (request.StaffId.HasValue)
        {
            var staffExists = await _context.BusinessUsers
                .AnyAsync(b => b.Id == request.StaffId);

            if (!staffExists)
                return BadRequest("Invalid staff.");
        }

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
            ServiceId = request.ServiceId,
            StaffId = request.StaffId,
            CreatedByUserId = _tenant.UserId!.Value,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = "Scheduled",
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.Appointments.Add(appointment);

        await _context.SaveChangesAsync();

        // SignalR: Notify all users in the tenant group
        await _hubContext.Clients
            .Group(_tenant.TenantId.ToString())
            .SendAsync("AppointmentUpdated");

        // Fetch related entities for response
        var service = appointment.ServiceId.HasValue ? await _context.Services.FindAsync(appointment.ServiceId) : null;
        var staff = appointment.StaffId.HasValue ? await _context.BusinessUsers.FirstOrDefaultAsync(bu => bu.Id == appointment.StaffId) : null;
        string? staffName = null;
        if (staff != null)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == staff.UserId);
            staffName = user?.FullName;
        }

        return CreatedAtAction(nameof(GetById),
            new { id = appointment.Id },
            new AppointmentDto(
                appointment.Id,
                appointment.ClientId,
                client.FullName,
                appointment.ServiceId,
                service?.Name,
                appointment.StaffId,
                staffName,
                appointment.StartTime,
                appointment.EndTime,
                appointment.Status,
                appointment.Notes,
                appointment.CreatedAt,
                appointment.IsDocumented ?? false
            ));
    }

    // PUT /appointments/{id}
    [Authorize(Policy = "manage_appointments")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateAppointmentRequest request)
    {
        var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == id);
        if (appointment == null)
            return NotFound();

        // ✅ Time validation (only if provided)
        if (request.StartTime != default && request.EndTime != default &&
            request.EndTime <= request.StartTime)
        {
            return BadRequest("End time must be after start time.");
        }

        // ✅ Normalize statuses (fix ALL your bugs)
        var currentStatus = appointment.Status?.Trim();
        var newStatus = request.Status?.Trim() ?? appointment.Status;

        // ✅ Validate status (case insensitive)
        if (!AppointmentStatuses.All
            .Any(s => s.Equals(newStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest($"Invalid status. Allowed: {string.Join(", ", AppointmentStatuses.All)}");
        }

        // ✅ Map to correct casing (important!)
        currentStatus = AppointmentStatuses.All
            .First(s => s.Equals(currentStatus, StringComparison.OrdinalIgnoreCase));

        newStatus = AppointmentStatuses.All
            .First(s => s.Equals(newStatus, StringComparison.OrdinalIgnoreCase));

        // ✅ Allowed transitions (FIXED)
        var allowedTransitions = new Dictionary<string, string[]>
        {
            { AppointmentStatuses.Scheduled, new[] { AppointmentStatuses.Waiting, AppointmentStatuses.Cancelled, AppointmentStatuses.NoShow } },
            { AppointmentStatuses.Waiting, new[] { AppointmentStatuses.InProgress, AppointmentStatuses.Cancelled, AppointmentStatuses.NoShow } },
            { AppointmentStatuses.InProgress, new[] { AppointmentStatuses.Completed, AppointmentStatuses.Cancelled, AppointmentStatuses.NoShow } },

            { AppointmentStatuses.Completed, new[] { AppointmentStatuses.Scheduled, AppointmentStatuses.Waiting } },
            { AppointmentStatuses.Cancelled, new[] { AppointmentStatuses.Scheduled } },
            { AppointmentStatuses.NoShow, new[] { AppointmentStatuses.Scheduled } }
        };

        if (newStatus != currentStatus &&
            (!allowedTransitions.TryGetValue(currentStatus, out var allowed) || !allowed.Contains(newStatus)))
        {
            return BadRequest($"Invalid status transition from {currentStatus} to {newStatus}.");
        }

        // ✅ Only one InProgress
        if (newStatus == AppointmentStatuses.InProgress)
        {
            var exists = await _context.Appointments.AnyAsync(a =>
                a.TenantId == appointment.TenantId &&
                a.Status == AppointmentStatuses.InProgress &&
                a.Id != appointment.Id);

            if (exists)
                return BadRequest("Only one appointment can be InProgress at a time.");
        }

        // ✅ Update fields safely
        if (request.StartTime != default)
            appointment.StartTime = request.StartTime;

        if (request.EndTime != default)
            appointment.EndTime = request.EndTime;

        appointment.Status = newStatus;
        appointment.Notes = request.Notes;
        appointment.ServiceId = request.ServiceId;
        appointment.StaffId = request.StaffId;
       
       if (request.IsDocumented.HasValue)
        {
            appointment.IsDocumented = request.IsDocumented.Value;
        }

        await _context.SaveChangesAsync();
        await _hubContext.Clients
        .Group(_tenant.TenantId.ToString())
        .SendAsync("AppointmentUpdated");

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == appointment.ClientId);
        var service = appointment.ServiceId.HasValue ? await _context.Services.FindAsync(appointment.ServiceId) : null;
        var staff = appointment.StaffId.HasValue ? await _context.BusinessUsers.FirstOrDefaultAsync(b => b.Id == appointment.StaffId) : null;

        string? staffName = null;
        if (staff != null)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == staff.UserId);
            staffName = user?.FullName;
        }

        return Ok(new AppointmentDto(
            appointment.Id,
            appointment.ClientId,
            client?.FullName ?? "",
            appointment.ServiceId,
            service?.Name,
            appointment.StaffId,
            staffName,
            appointment.StartTime,
            appointment.EndTime,
            appointment.Status,
            appointment.Notes,
            appointment.CreatedAt,
            appointment.IsDocumented ?? false
        ));
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