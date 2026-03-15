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

        public async Task<List<object>> GetRecentActivityAsync()
        {
            var tenantId = _tenantContext!.TenantId;
            var activity = await _db.Appointments
                .Where(a => a.TenantId == tenantId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(10)
                .Select(a => new
                {
                    id = a.Id,
                    type = "appointment",
                    title = "Appointment created",
                    clientName = a.Client != null ? a.Client.FullName : null,
                    serviceName = a.Service != null ? a.Service.Name : null,
                    staffName = a.Staff != null && a.Staff.User != null ? a.Staff.User.FullName : null,
                    startTime = a.StartTime,
                    description = a.Notes,
                    timestamp = a.CreatedAt
                })
                .ToListAsync();
            return activity.Cast<object>().ToList();
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
