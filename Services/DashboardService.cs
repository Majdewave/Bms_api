using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;

namespace Clienta.Api.Services
{
    public class DashboardService
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;
        private readonly IDepartmentAccessService _departmentAccessService;

        public DashboardService(
            AppDbContext db,
            ITenantContext tenantContext,
            IDepartmentAccessService departmentAccessService)
        {
            _db = db;
            _tenantContext = tenantContext;
            _departmentAccessService = departmentAccessService;
        }

        public async Task<IEnumerable<ActivityDto>> GetRecentActivityAsync()
        {
            var tenantId = _tenantContext!.TenantId;
            var accessContext = await _departmentAccessService.GetCurrentUserAccessContextAsync();

            var scopedAppointments = _departmentAccessService.ApplyAppointmentVisibility(
                _db.Appointments.Where(a => a.TenantId == tenantId),
                accessContext);

            var appointmentCreatedEvents = await scopedAppointments
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

            var appointmentCompletedEvents = await scopedAppointments
                .Where(a => a.Status == "Completed")
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

            var scopedVisitSummaries = _departmentAccessService.ApplyVisitSummaryVisibility(
                _db.VisitSummaries.Where(v => v.TenantId == tenantId),
                accessContext);

            var visitSummaryEvents = await scopedVisitSummaries
                .Select(v => new ActivityDto
                {
                    id = v.Id,
                    type = "visit_summary_created",
                    title = "Visit summary created",
                    clientName = _db.Clients
                        .Where(c => c.Id == v.ClientId)
                        .Select(c => c.FullName)
                        .FirstOrDefault(),
                    staffName = v.StaffId.HasValue
                        ? _db.Users
                            .Where(u => u.Id == v.StaffId.Value)
                            .Select(u => u.FullName)
                            .FirstOrDefault()
                        : null,
                    serviceName = null,
                    performedBy = v.StaffId.HasValue
                        ? _db.Users
                            .Where(u => u.Id == v.StaffId.Value)
                            .Select(u => u.FullName)
                            .FirstOrDefault()
                        : "Admin",
                    timestamp = v.CreatedAt
                })
                .ToListAsync();

            var clientEvents = new List<ActivityDto>();
            var staffEvents = new List<ActivityDto>();
            var auditEvents = new List<ActivityDto>();
            var userDeletedEvents = new List<ActivityDto>();
            var clientDeletedEvents = new List<ActivityDto>();

            if (!accessContext.HasDepartmentFilter)
            {
                clientEvents = await _db.Clients
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

                staffEvents = await _db.BusinessUsers
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

                auditEvents = await _db.AuditLogs
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

                userDeletedEvents = await _db.AuditLogs
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

                clientDeletedEvents = await _db.AuditLogs
                    .Where(a => a.TenantId == tenantId && a.ActionType == "client_deleted")
                    .Include(a => a.User)
                    .Select(a => new ActivityDto
                    {
                        id = a.Id,
                        type = "client_deleted",
                        title = "Client deleted",
                        clientName = a.NewValues,
                        staffName = null,
                        serviceName = null,
                        performedBy = a.PerformedBy ?? (a.User != null ? a.User.FullName : "Admin"),
                        timestamp = a.CreatedAt
                    })
                    .ToListAsync();
            }

            var allEvents = appointmentCreatedEvents
                .Concat(appointmentCompletedEvents)
                .Concat(visitSummaryEvents)
                .Concat(clientEvents)
                .Concat(staffEvents)
                .Concat(auditEvents)
                .Concat(userDeletedEvents)
                .Concat(clientDeletedEvents)
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
            var businessTimeZone = ResolveBusinessTimeZone();
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, businessTimeZone);
            var localTodayStart = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, DateTimeKind.Unspecified);
            var localTomorrowStart = localTodayStart.AddDays(1);
            var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localTodayStart, businessTimeZone);
            var tomorrowStartUtc = TimeZoneInfo.ConvertTimeToUtc(localTomorrowStart, businessTimeZone);

            var accessContext = await _departmentAccessService.GetCurrentUserAccessContextAsync();

            var scopedAppointments = _departmentAccessService.ApplyAppointmentVisibility(
                _db.Appointments.Where(a => a.TenantId == tenantId),
                accessContext);

            var scopedClientIds = await scopedAppointments
                .Select(a => a.ClientId)
                .Distinct()
                .ToListAsync();

            var scopedVisitSummaryClientIds = await _departmentAccessService
                .ApplyVisitSummaryVisibility(
                    _db.VisitSummaries.Where(v => v.TenantId == tenantId),
                    accessContext)
                .Select(v => v.ClientId)
                .Distinct()
                .ToListAsync();

            var allScopedClientIds = scopedClientIds
                .Concat(scopedVisitSummaryClientIds)
                .Distinct()
                .ToList();

            // Clients belong to tenant scope, not department scope.
            var totalClients = await _db.Clients.CountAsync(c => c.TenantId == tenantId);

            var notDocumentedClientsCount = await scopedAppointments
                .Where(a => !a.IsDocumented)
                .Select(a => a.ClientId)
                .Distinct()
                .CountAsync();

            var appointmentsToday = await scopedAppointments
                .CountAsync(a => a.StartTime >= todayStartUtc && a.StartTime < tomorrowStartUtc);

            var completedAppointmentsToday = await scopedAppointments
                .CountAsync(a => a.StartTime >= todayStartUtc && a.StartTime < tomorrowStartUtc && a.Status == AppointmentStatuses.Completed);

            var noShowToday = await scopedAppointments
                .CountAsync(a => a.StartTime >= todayStartUtc && a.StartTime < tomorrowStartUtc && a.Status == AppointmentStatuses.NoShow);

            var upcomingAppointments = await scopedAppointments
                .Where(a => a.StartTime >= nowUtc)
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
                    status = a.Status,
                    isDocumented = a.IsDocumented
                })
                .ToListAsync();

            var upcomingAppointmentsLocal = upcomingAppointments
                .Select(a => new
                {
                    a.id,
                    a.clientId,
                    a.clientName,
                    a.serviceName,
                    a.staffName,
                    startTime = DateTime.SpecifyKind(a.startTime, DateTimeKind.Unspecified),
                    endTime = DateTime.SpecifyKind(a.endTime, DateTimeKind.Unspecified),
                    a.status,
                    a.isDocumented
                })
                .ToList();

            return new DashboardStats
            {
                TotalClients = totalClients,
                NotDocumentedClientsCount = notDocumentedClientsCount,
                AppointmentsToday = appointmentsToday,
                CompletedAppointmentsToday = completedAppointmentsToday,
                NoShowToday = noShowToday,
                UpcomingAppointments = upcomingAppointmentsLocal
            };
        }

        private static TimeZoneInfo ResolveBusinessTimeZone()
        {
            var primaryId = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Israel Standard Time"
                : "Asia/Jerusalem";

            var fallbackId = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Asia/Jerusalem"
                : "Israel Standard Time";

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(primaryId);
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(fallbackId);
                }
                catch
                {
                    return TimeZoneInfo.Utc;
                }
            }
        }
    }

    public class DashboardStats
    {
        public int TotalClients { get; set; }
        public int NotDocumentedClientsCount { get; set; }
        public int AppointmentsToday { get; set; }
        public int CompletedAppointmentsToday { get; set; }
        public int NoShowToday { get; set; }
        public object UpcomingAppointments { get; set; } = new List<object>();
    }
}
