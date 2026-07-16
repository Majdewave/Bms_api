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
    private readonly IDepartmentAccessService _departmentAccessService;
    private readonly IPlanEnforcementService _planEnforcement;
    private readonly IHubContext<AppointmentsHub> _hubContext;

    public AppointmentsController(
        AppDbContext context,
        ITenantContext tenant,
        IDepartmentAccessService departmentAccessService,
        IPlanEnforcementService planEnforcement,
        IHubContext<AppointmentsHub> hubContext)
    {
        _context = context;
        _tenant = tenant;
        _departmentAccessService = departmentAccessService;
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
            .OrderBy(a => a.QueueNumber ?? int.MaxValue)
            .ThenBy(a => a.StartTime)
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
                inProgress.QueueNumber,
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
                nextWaiting.QueueNumber,
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
    public async Task<IActionResult> GetAll([FromQuery] Guid? clientId)
    {
        var query = _context.Appointments
            .Where(a => a.TenantId == _tenant.TenantId);

        if (!clientId.HasValue || clientId.Value == Guid.Empty)
        {
            var twoWeeksAgo = DateTime.UtcNow.AddDays(-7);
            query = query.Where(a => a.StartTime >= twoWeeksAgo);
        }

        if (clientId.HasValue && clientId.Value != Guid.Empty)
        {
            query = query.Where(a => a.ClientId == clientId.Value);
        }

        var scopedAppointments = await ApplyStaffDepartmentVisibilityAsync(query);

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
            .ThenBy(a => a.Status == AppointmentStatuses.Waiting ? (a.QueueNumber ?? int.MaxValue) : int.MaxValue)
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
                a.QueueNumber,
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
                a.QueueNumber,
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

        var normalizedStartTimeUtc = NormalizeToUtc(request.StartTime);
        var normalizedEndTimeUtc = NormalizeToUtc(request.EndTime);
        var appointmentDate = NormalizeAppointmentDate(normalizedStartTimeUtc);

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ClientId = request.ClientId,
            ServiceId = request.ServiceId,
            DepartmentId = selectedService?.DepartmentId,
            StaffId = request.StaffId,
            CreatedByUserId = _tenant.UserId ?? Guid.Empty,
            StartTime = normalizedStartTimeUtc,
            EndTime = normalizedEndTimeUtc,
            AppointmentDate = appointmentDate,
            Status = AppointmentStatuses.Scheduled,
            QueueNumber = null,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync();

        await _hubContext.Clients.Group(_tenant.TenantId.ToString()).SendAsync("AppointmentUpdated");

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
        {
            appointment.StartTime = NormalizeToUtc(request.StartTime);
        }

        if (request.EndTime != default)
            appointment.EndTime = NormalizeToUtc(request.EndTime);

        // Self-heal persisted optimization field from StartTime on every update.
        var appointmentDate = NormalizeAppointmentDate(appointment.StartTime);
        appointment.AppointmentDate = appointmentDate;

        appointment.Status = newStatus;
        if (request.Notes != null)
            appointment.Notes = request.Notes;

        if (newStatus == AppointmentStatuses.Waiting)
        {
            if (currentStatus != AppointmentStatuses.Waiting)
            {
                var maxQueue = await _context.Appointments
                    .Where(a => a.TenantId == appointment.TenantId
                        && a.StartTime.Date == appointmentDate.Date
                        && a.Status == AppointmentStatuses.Waiting
                        && a.Id != appointment.Id)
                    .MaxAsync(a => (int?)a.QueueNumber) ?? 0;

                appointment.QueueNumber = maxQueue + 1;
            }
            else if (request.QueueNumber.HasValue)
            {
                if (request.QueueNumber.Value <= 0)
                    return BadRequest("QueueNumber must be greater than zero.");

                var queueConflict = await _context.Appointments.AnyAsync(a =>
                    a.TenantId == appointment.TenantId
                    && a.StartTime.Date == appointmentDate.Date
                    && a.Status == AppointmentStatuses.Waiting
                    && a.QueueNumber == request.QueueNumber.Value
                    && a.Id != appointment.Id);

                if (queueConflict)
                    return BadRequest("QueueNumber must be unique for waiting appointments on the same appointment date.");

                appointment.QueueNumber = request.QueueNumber.Value;
            }
        }
        else if (request.QueueNumber.HasValue)
        {
            return BadRequest("QueueNumber can only be set for waiting appointments.");
        }

        // Keep DepartmentId as an appointment-time snapshot.
        // It should only change when ServiceId is explicitly changed on the appointment.
        if (request.ServiceId.HasValue)
        {
            appointment.ServiceId = request.ServiceId;
            appointment.DepartmentId = selectedService?.DepartmentId;
        }

        if (request.StaffId.HasValue)
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
        await _hubContext.Clients.Group(_tenant.TenantId.ToString()).SendAsync("AppointmentUpdated");

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

    // POST /appointments/waiting-queue/reorder
    [Authorize(Policy = "manage_appointments")]
    [HttpPost("waiting-queue/reorder")]
    public async Task<IActionResult> ReorderWaitingQueue([FromBody] ReorderWaitingQueueRequest request)
    {
        if (request?.Items == null || request.Items.Count == 0)
            return BadRequest("Items are required.");

        if (request.Items.Any(i => i.QueueNumber <= 0))
            return BadRequest("QueueNumber values must be greater than zero.");

        var duplicateIds = request.Items
            .GroupBy(i => i.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateIds.Count > 0)
            return BadRequest("Duplicate appointment ids are not allowed.");

        var duplicateQueueNumbers = request.Items
            .GroupBy(i => i.QueueNumber)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateQueueNumbers.Count > 0)
            return BadRequest("QueueNumber values must be unique.");

        var orderedQueueNumbers = request.Items
            .Select(i => i.QueueNumber)
            .OrderBy(x => x)
            .ToList();
        for (var i = 0; i < orderedQueueNumbers.Count; i++)
        {
            if (orderedQueueNumbers[i] != i + 1)
                return BadRequest("QueueNumber values must be sequential starting at 1.");
        }

        var appointmentIds = request.Items.Select(i => i.Id).ToList();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var appointments = await _context.Appointments
                .Where(a => a.TenantId == _tenant.TenantId && appointmentIds.Contains(a.Id))
                .ToListAsync();

            if (appointments.Count != appointmentIds.Count)
                return BadRequest("One or more appointments were not found.");

            if (appointments.Any(a => a.Status != AppointmentStatuses.Waiting))
                return BadRequest("Only waiting appointments can be reordered.");

            var appointmentDate = NormalizeAppointmentDate(appointments[0].StartTime);
            if (appointments.Any(a => NormalizeAppointmentDate(a.StartTime) != appointmentDate))
                return BadRequest("All reordered appointments must belong to the same appointment date.");

            var allWaitingForDate = await _context.Appointments
                .Where(a => a.TenantId == _tenant.TenantId
                    && a.Status == AppointmentStatuses.Waiting
                    && a.StartTime.Date == appointmentDate.Date)
                .Select(a => a.Id)
                .ToListAsync();

            if (allWaitingForDate.Count != appointmentIds.Count)
                return BadRequest("Reorder payload must include all waiting appointments for the appointment date.");

            var allWaitingSet = allWaitingForDate.ToHashSet();
            if (appointmentIds.Any(id => !allWaitingSet.Contains(id)))
                return BadRequest("Reorder payload contains invalid appointments for this waiting queue.");

            var queueLookup = request.Items.ToDictionary(i => i.Id, i => i.QueueNumber);

            // Two-phase update avoids transient unique index conflicts when swapping queue numbers.
            // Phase 1 writes temporary negative values, phase 2 writes final positive sequence.
            foreach (var appointment in appointments)
            {
                // Self-heal persisted optimization field from StartTime.
                appointment.AppointmentDate = NormalizeAppointmentDate(appointment.StartTime);
                appointment.QueueNumber = -queueLookup[appointment.Id];
            }

            await _context.SaveChangesAsync();

            foreach (var appointment in appointments)
            {
                appointment.QueueNumber = queueLookup[appointment.Id];
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return BadRequest("Invalid queue update. Queue numbers must remain unique per tenant, appointment date, and waiting status.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        await _hubContext.Clients.Group(_tenant.TenantId.ToString())
            .SendAsync("WaitingQueueReordered");

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
            appointment.QueueNumber,
            appointment.Notes,
            appointment.CreatedAt,
            appointment.IsDocumented,
            await _context.ClientConsents.AnyAsync(c => c.AppointmentId == appointment.Id)
        );
    }

    private static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    // Derived persistence key for indexing/uniqueness/queue grouping only.
    // AppointmentDate is not part of the public API contract and must be derived from StartTime.
    private static DateTime NormalizeAppointmentDate(DateTime utcStartTime)
    {
        return new DateTime(utcStartTime.Year, utcStartTime.Month, utcStartTime.Day, 0, 0, 0, DateTimeKind.Utc);
    }

    private async Task<IQueryable<Appointment>> ApplyStaffDepartmentVisibilityAsync(IQueryable<Appointment> query)
    {
        var accessContext = await _departmentAccessService.GetCurrentUserAccessContextAsync();
        return _departmentAccessService.ApplyAppointmentVisibility(query, accessContext);
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
