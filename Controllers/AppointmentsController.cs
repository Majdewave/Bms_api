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

    // GET /appointments/queue
    [Authorize(Policy = "manage_appointments")]
    [HttpGet("queue")]
    public async Task<IActionResult> GetQueue()
    {
        var tenantId = _tenant.TenantId;
        var scopedAppointments = await ApplyStaffDepartmentVisibilityAsync(
            _context.Appointments.Where(a => a.TenantId == tenantId));

        var inProgress = await scopedAppointments
            .Include(a => a.Client)
            .Include(a => a.Service)
            .Include(a => a.Department)
            .Include(a => a.Staff)
                .ThenInclude(s => s.User)
            .Where(a => a.Status == AppointmentStatuses.InProgress)
            .OrderBy(a => a.StartTime)
            .FirstOrDefaultAsync();

        var nextWaiting = await scopedAppointments
            .Include(a => a.Client)
            .Include(a => a.Service)
            .Include(a => a.Department)
            .Include(a => a.Staff)
                .ThenInclude(s => s.User)
            .Where(a => a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.StartTime)
            .FirstOrDefaultAsync();

        var waitingCount = await scopedAppointments
            .CountAsync(a => a.Status == AppointmentStatuses.Waiting);

        return Ok(new
        {
            current = inProgress == null ? null : new AppointmentDto(
                inProgress.Id,
                inProgress.ClientId,
                inProgress.Client.FullName,
                inProgress.ServiceId,
                inProgress.Service != null ? inProgress.Service.Name : null,
                inProgress.DepartmentId,
                inProgress.Department != null ? inProgress.Department.Name : null,
                inProgress.Department != null ? inProgress.Department.Color : null,
                inProgress.StaffId,
                inProgress.Staff != null ? inProgress.Staff.User.FullName : null,
                inProgress.StartTime.ToLocalTime(),
                inProgress.EndTime.ToLocalTime(),
                inProgress.Status,
                inProgress.Notes,
                inProgress.CreatedAt,
                inProgress.IsDocumented,
                await _context.ClientConsents.AnyAsync(c => c.AppointmentId == inProgress.Id)
            ),
            next = nextWaiting == null ? null : new AppointmentDto(
                nextWaiting.Id,
                nextWaiting.ClientId,
                nextWaiting.Client.FullName,
                nextWaiting.ServiceId,
                nextWaiting.Service != null ? nextWaiting.Service.Name : null,
                nextWaiting.DepartmentId,
                nextWaiting.Department != null ? nextWaiting.Department.Name : null,
                nextWaiting.Department != null ? nextWaiting.Department.Color : null,
                nextWaiting.StaffId,
                nextWaiting.Staff != null ? nextWaiting.Staff.User.FullName : null,
                nextWaiting.StartTime.ToLocalTime(),
                nextWaiting.EndTime.ToLocalTime(),
                nextWaiting.Status,
                nextWaiting.Notes,
                nextWaiting.CreatedAt,
                nextWaiting.IsDocumented,
                await _context.ClientConsents.AnyAsync(c => c.AppointmentId == nextWaiting.Id)
            ),
            waitingCount
        });
    }

    // GET /appointments
    [Authorize(Policy = "manage_appointments")]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var twoWeeksAgo = DateTime.UtcNow.AddDays(-14);

        var scopedAppointments = await ApplyStaffDepartmentVisibilityAsync(
            _context.Appointments
                .Where(a =>
                    a.TenantId == _tenant.TenantId &&
                    a.StartTime >= twoWeeksAgo));

        var appointments = await scopedAppointments
            .Include(a => a.Client)
            .Include(a => a.Service)
            .Include(a => a.Department)
            .Include(a => a.Staff)
                .ThenInclude(s => s.User)
            .OrderBy(a =>
                a.Status == AppointmentStatuses.InProgress ? 0 :
                a.Status == AppointmentStatuses.Waiting ? 1 :
                2
            )
            .ThenBy(a => a.StartTime)
            .Select(a => new AppointmentDto(
                a.Id,
                a.ClientId,
                a.Client.FullName,
                a.ServiceId,
                a.Service != null ? a.Service.Name : null,
                a.DepartmentId,
                a.Department != null ? a.Department.Name : null,
                a.Department != null ? a.Department.Color : null,
                a.StaffId,
                a.Staff != null ? a.Staff.User.FullName : null,
                a.StartTime.ToLocalTime(),
                a.EndTime.ToLocalTime(),
                a.Status,
                a.Notes,
                a.CreatedAt,
                a.IsDocumented,
                _context.ClientConsents.Any(c => c.AppointmentId == a.Id)
            ))
            .ToListAsync();

        return Ok(appointments);
    }

    // GET /appointments/{id}
    [Authorize(Policy = "manage_appointments")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var scopedAppointments = await ApplyStaffDepartmentVisibilityAsync(
            _context.Appointments.Where(a => a.Id == id));

        var appointment = await scopedAppointments
            .Include(a => a.Client)
            .Include(a => a.Service)
            .Include(a => a.Department)
            .Include(a => a.Staff)
                .ThenInclude(s => s.User)
            .Select(a => new AppointmentDto(
                a.Id,
                a.ClientId,
                a.Client.FullName,
                a.ServiceId,
                a.Service != null ? a.Service.Name : null,
                a.DepartmentId,
                a.Department != null ? a.Department.Name : null,
                a.Department != null ? a.Department.Color : null,
                a.StaffId,
                a.Staff != null ? a.Staff.User.FullName : null,
                a.StartTime.ToLocalTime(),
                a.EndTime.ToLocalTime(),
                a.Status,
                a.Notes,
                a.CreatedAt,
                a.IsDocumented,
                _context.ClientConsents.Any(c => c.AppointmentId == a.Id)
            ))
            .FirstOrDefaultAsync();

        if (appointment == null)
            return NotFound();

        return Ok(appointment);
    }

    // POST /appointments
    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentRequest request)
    {
        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == request.ClientId && c.TenantId == _tenant.TenantId);

        if (client == null)
            return BadRequest("Invalid client.");

        if (request.StartTime != default && request.EndTime != default &&
            request.EndTime <= request.StartTime)
        {
            return BadRequest("End time must be after start time.");
        }

        Service? selectedService = null;
        if (request.ServiceId.HasValue)
        {
            selectedService = await _context.Services
                .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.TenantId == _tenant.TenantId);

            if (selectedService == null)
                return BadRequest("Invalid service.");
        }

        if (request.StaffId.HasValue)
        {
            var staffExists = await _context.BusinessUsers
                .AnyAsync(b => b.Id == request.StaffId && b.TenantId == _tenant.TenantId);

            if (!staffExists)
                return BadRequest("Invalid staff.");
        }

        if (request.StaffId.HasValue && request.ServiceId.HasValue)
        {
            var isAllowed = await IsServiceAllowedForStaffAsync(request.StaffId.Value, selectedService?.DepartmentId);
            if (!isAllowed)
                return BadRequest("Selected service is not available for this staff member.");
        }

        try
        {
            await _planEnforcement.EnsureMessageLimitAsync(_tenant.TenantId);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        if (_tenant.UserId == null)
            return Unauthorized("User not found in token");

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ClientId = request.ClientId,
            ServiceId = request.ServiceId,
            DepartmentId = selectedService?.DepartmentId,
            StaffId = request.StaffId,
            CreatedByUserId = _tenant.UserId ?? Guid.Empty,
            StartTime = request.StartTime.ToUniversalTime(),
            EndTime = request.EndTime.ToUniversalTime(),
            Status = AppointmentStatuses.Scheduled,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("AppointmentUpdated");

        var createdAppointment = await _context.Appointments
            .Include(a => a.Client)
            .Include(a => a.Service)
            .Include(a => a.Department)
            .Include(a => a.Staff)
                .ThenInclude(s => s.User)
            .FirstOrDefaultAsync(a => a.Id == appointment.Id);

        if (createdAppointment == null)
            return StatusCode(500, "Failed to load created appointment.");

        return CreatedAtAction(nameof(GetById),
            new { id = appointment.Id },
            await BuildAppointmentDtoAsync(createdAppointment));
    }

    // PUT /appointments/{id}
    [Authorize(Policy = "manage_appointments")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateAppointmentRequest request)
    {
        var scopedAppointments = await ApplyStaffDepartmentVisibilityAsync(
            _context.Appointments.Where(a => a.Id == id));

        var appointment = await scopedAppointments.FirstOrDefaultAsync();
        if (appointment == null)
            return NotFound();

        if (request.StartTime != default && request.EndTime != default &&
            request.EndTime <= request.StartTime)
        {
            return BadRequest("End time must be after start time.");
        }

        var currentStatus = appointment.Status?.Trim();
        var newStatus = request.Status?.Trim() ?? appointment.Status;

        if (!AppointmentStatuses.All
            .Any(s => s.Equals(newStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest($"Invalid status. Allowed: {string.Join(", ", AppointmentStatuses.All)}");
        }

        currentStatus = AppointmentStatuses.All
            .First(s => s.Equals(currentStatus, StringComparison.OrdinalIgnoreCase));

        newStatus = AppointmentStatuses.All
            .First(s => s.Equals(newStatus, StringComparison.OrdinalIgnoreCase));

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

        if (newStatus == AppointmentStatuses.InProgress)
        {
            var exists = await _context.Appointments.AnyAsync(a =>
                a.TenantId == appointment.TenantId &&
                a.Status == AppointmentStatuses.InProgress &&
                a.Id != appointment.Id);

            if (exists)
                return BadRequest("Only one appointment can be InProgress at a time.");
        }

        Service? selectedService = null;
        if (request.ServiceId.HasValue)
        {
            selectedService = await _context.Services
                .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.TenantId == _tenant.TenantId);

            if (selectedService == null)
                return BadRequest("Invalid service.");
        }

        if (request.StaffId.HasValue)
        {
            var staffExists = await _context.BusinessUsers
                .AnyAsync(b => b.Id == request.StaffId && b.TenantId == _tenant.TenantId);

            if (!staffExists)
                return BadRequest("Invalid staff.");
        }

        if (request.StaffId.HasValue && request.ServiceId.HasValue)
        {
            var isAllowed = await IsServiceAllowedForStaffAsync(request.StaffId.Value, selectedService?.DepartmentId);
            if (!isAllowed)
                return BadRequest("Selected service is not available for this staff member.");
        }

        if (request.StartTime != default)
            appointment.StartTime = DateTime.SpecifyKind(request.StartTime, DateTimeKind.Utc);

        if (request.EndTime != default)
            appointment.EndTime = DateTime.SpecifyKind(request.EndTime, DateTimeKind.Utc);

        appointment.Status = newStatus;
        appointment.Notes = request.Notes;
        appointment.ServiceId = request.ServiceId;
        appointment.DepartmentId = selectedService?.DepartmentId;
        appointment.StaffId = request.StaffId;

        if (request.IsDocumented.HasValue)
        {
            appointment.IsDocumented = request.IsDocumented.Value;

            var appointmentClient = await _context.Clients.FindAsync(appointment.ClientId);

            if (appointmentClient != null)
            {
                if (!appointment.IsDocumented)
                {
                    appointmentClient.IsNotDocumented = true;
                }
                else
                {
                    var hasUndocumented = _context.Appointments
                        .Any(a => a.ClientId == appointment.ClientId && !a.IsDocumented);

                    appointmentClient.IsNotDocumented = hasUndocumented;
                }
            }
        }

        await _context.SaveChangesAsync();
        await _hubContext.Clients.All.SendAsync("AppointmentUpdated");

        var updatedAppointment = await _context.Appointments
            .Include(a => a.Client)
            .Include(a => a.Service)
            .Include(a => a.Department)
            .Include(a => a.Staff)
                .ThenInclude(s => s.User)
            .FirstOrDefaultAsync(a => a.Id == appointment.Id);

        if (updatedAppointment == null)
            return StatusCode(500, "Failed to load updated appointment.");

        return Ok(await BuildAppointmentDtoAsync(updatedAppointment));
    }

    // DELETE /appointments/{id}
    [Authorize(Policy = "manage_appointments")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var scopedAppointments = await ApplyStaffDepartmentVisibilityAsync(
            _context.Appointments.Where(a => a.Id == id));

        var appointment = await scopedAppointments.FirstOrDefaultAsync();

        if (appointment == null)
            return NotFound();

        _context.Appointments.Remove(appointment);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<AppointmentDto> BuildAppointmentDtoAsync(Appointment appointment)
    {
        return new AppointmentDto(
            appointment.Id,
            appointment.ClientId,
            appointment.Client?.FullName ?? string.Empty,
            appointment.ServiceId,
            appointment.Service?.Name,
            appointment.DepartmentId,
            appointment.Department?.Name,
            appointment.Department?.Color,
            appointment.StaffId,
            appointment.Staff?.User?.FullName,
            appointment.StartTime.ToLocalTime(),
            appointment.EndTime.ToLocalTime(),
            appointment.Status,
            appointment.Notes,
            appointment.CreatedAt,
            appointment.IsDocumented,
            await _context.ClientConsents.AnyAsync(c => c.AppointmentId == appointment.Id)
        );
    }

    private async Task<IQueryable<Appointment>> ApplyStaffDepartmentVisibilityAsync(IQueryable<Appointment> query)
    {
        if (_tenant.UserId == null || _tenant.UserId == Guid.Empty)
            return query;

        var currentStaff = await _context.BusinessUsers
            .Include(b => b.StaffDepartments)
            .FirstOrDefaultAsync(b =>
                b.TenantId == _tenant.TenantId &&
                b.UserId == _tenant.UserId);

        if (currentStaff == null)
            return query;

        var departmentIds = currentStaff.StaffDepartments
            .Select(sd => sd.DepartmentId)
            .Distinct()
            .ToList();

        if (departmentIds.Count == 0)
            return query;

        return query.Where(a =>
            !a.DepartmentId.HasValue || departmentIds.Contains(a.DepartmentId.Value));
    }

    private async Task<bool> IsServiceAllowedForStaffAsync(Guid staffId, Guid? serviceDepartmentId)
    {
        var departmentIds = await _context.StaffDepartments
            .Where(sd => sd.TenantId == _tenant.TenantId && sd.StaffId == staffId)
            .Select(sd => sd.DepartmentId)
            .Distinct()
            .ToListAsync();

        if (departmentIds.Count == 0)
            return true;

        if (!serviceDepartmentId.HasValue)
            return true;

        return departmentIds.Contains(serviceDepartmentId.Value);
    }
}
