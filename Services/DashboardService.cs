using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services
{
    public class DashboardService
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;
        public DashboardService(AppDbContext db, ITenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async Task<IEnumerable<ActivityDto>> GetRecentActivityAsync()
        {
            var tenantId = _tenantContext!.TenantId;

            var appointmentCreatedEvents = await _db.Appointments
                .Where(a => a.TenantId == tenantId)
                .Include(a => a.Client)
                .Include(a => a.Staff).ThenInclude(st => st.User)
                .Include(a => a.Service)
                .Select(a => new ActivityDto
                {
                    id = a.Id,
                    type = "appointment_created",
                    title = "Appointment created",
                    clientName = a.Client != null ? a.Client.FullName : null,
                    staffName = a.Staff != null && a.Staff.User != null ? a.Staff.User.FullName : null,
                    serviceName = a.Service != null ? a.Service.Name : null,
                    performedBy = a.Staff != null && a.Staff.User != null ? a.Staff.User.FullName : "Admin",
                    timestamp = a.CreatedAt
                })
                .ToListAsync();

            var appointmentCompletedEvents = await _db.Appointments
                .Where(a => a.TenantId == tenantId && a.Status == "Completed")
                .Include(a => a.Client)
                .Include(a => a.Staff).ThenInclude(st => st.User)
                .Include(a => a.Service)
                .Select(a => new ActivityDto
                {
                    id = a.Id,
                    type = "appointment_completed",
                    title = "Appointment completed",
                    clientName = a.Client != null ? a.Client.FullName : null,
                    staffName = a.Staff != null && a.Staff.User != null ? a.Staff.User.FullName : null,
                    serviceName = a.Service != null ? a.Service.Name : null,
                    performedBy = a.Staff != null && a.Staff.User != null ? a.Staff.User.FullName : "Admin",
                    timestamp = a.EndTime
                })
                .ToListAsync();

            var clientEvents = await _db.Clients
                .Where(c => c.TenantId == tenantId)
                .Select(c => new ActivityDto
                {
                    id = c.Id,
                    type = "client_created",
                    title = "Client created",
                    clientName = c.FullName,
                    staffName = null,
                    serviceName = null,
                    performedBy = "Admin",
                    timestamp = c.CreatedAt
                })
                .ToListAsync();

            var staffEvents = await _db.BusinessUsers
                .Where(s => s.TenantId == tenantId)
                .Include(s => s.User)
                .Select(s => new ActivityDto
                {
                    id = s.Id,
                    type = "staff_created",
                    title = "Staff member added",
                    clientName = null,
                    staffName = s.User != null ? s.User.FullName : null,
                    serviceName = null,
                    performedBy = s.User != null ? s.User.FullName : "Admin",
                    timestamp = s.User != null ? s.User.CreatedAt : DateTime.MinValue
                })
                .ToListAsync();

            var auditEvents = await _db.AuditLogs
                .Where(a => a.TenantId == tenantId && a.ActionType == "staff_deleted")
                .Include(a => a.User)
                .Select(a => new ActivityDto
                {
                    id = a.Id,
                    type = "staff_deleted",
                    title = "Staff member deleted",
                    clientName = null,
                    staffName = a.NewValues,
                    serviceName = null,
                    performedBy = a.PerformedBy ?? (a.User != null ? a.User.FullName : "Admin"),
                    timestamp = a.CreatedAt
                })
                .ToListAsync();

            var userDeletedEvents = await _db.AuditLogs
                .Where(a => a.TenantId == tenantId && a.ActionType == "user_deleted")
                .Include(a => a.User)
                .Select(a => new ActivityDto
                {
                    id = a.Id,
                    type = "user_deleted",
                    title = "User deleted",
                    clientName = null,
                    staffName = a.NewValues,
                    serviceName = null,
                    performedBy = a.PerformedBy ?? (a.User != null ? a.User.FullName : "Admin"),
                    timestamp = a.CreatedAt
                })
                .ToListAsync();

            var allEvents = appointmentCreatedEvents
                .Concat(appointmentCompletedEvents)
                .Concat(clientEvents)
                .Concat(staffEvents)
                .Concat(auditEvents)
                .Concat(userDeletedEvents)
                .OrderByDescending(e => e.timestamp)
                .Take(10);

            return allEvents;
        }


    public class ActivityDto
    {
        public Guid id { get; set; }
        public string type { get; set; }
        public string title { get; set; }
        public string? clientName { get; set; }
        public string? staffName { get; set; }
        public string? serviceName { get; set; }
        public string? performedBy { get; set; }
        public DateTime timestamp { get; set; }
    }


        public async Task<DashboardStats> GetDashboardStatsAsync()
        {
            var tenantId = _tenantContext!.TenantId;
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var totalClients = await _db.Clients.CountAsync(c => c.TenantId == tenantId);
            var appointmentsToday = await _db.Appointments.CountAsync(a => a.TenantId == tenantId && a.StartTime >= today && a.StartTime < tomorrow);
            var completedAppointmentsToday = await _db.Appointments.CountAsync(a => a.TenantId == tenantId && a.StartTime >= today && a.StartTime < tomorrow && a.Status == "Completed");
            var noShowToday = await _db.Appointments.CountAsync(a => a.TenantId == tenantId && a.StartTime >= today && a.StartTime < tomorrow && a.Status == "NoShow");

            var upcomingAppointments = await _db.Appointments
                .Where(a => a.TenantId == tenantId && a.StartTime >= tomorrow)
                .OrderBy(a => a.StartTime)
                .Take(5)
                .Select(a => new {
                    id = a.Id,
                    clientId = a.ClientId,
                    clientName = a.Client != null ? a.Client.FullName : null,
                    serviceName = a.Service != null ? a.Service.Name : null,
                    staffName = a.Staff != null && a.Staff.User != null ? a.Staff.User.FullName : null,
                    startTime = a.StartTime,
                    endTime = a.EndTime,
                    status = a.Status
                })
                .ToListAsync();

            return new DashboardStats
            {
                TotalClients = totalClients,
                AppointmentsToday = appointmentsToday,
                CompletedAppointmentsToday = completedAppointmentsToday,
                NoShowToday = noShowToday,
                UpcomingAppointments = upcomingAppointments
            };
        }
    }

    public class DashboardStats
    {
        public int TotalClients { get; set; }
        public int AppointmentsToday { get; set; }
        public int CompletedAppointmentsToday { get; set; }
        public int NoShowToday { get; set; }
        public object UpcomingAppointments { get; set; } = new List<object>();
    }
}
