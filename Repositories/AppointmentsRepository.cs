using Clienta.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Repositories
{
    public class AppointmentsRepository
    {
        private readonly AppDbContext _db;
        public AppointmentsRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<object>> GetRecentEvents()
        {
            var events = await _db.Appointments
                .OrderByDescending(a => a.CreatedAt)
                .Take(10)
                .Select(a => new
                {
                    Id = a.Id,
                    Title = "Appointment created",
                    ClientName = a.Client.FullName,
                    StaffName = a.Staff.User.FullName,
                    ServiceName = a.Service.Name,
                    Status = a.Status,
                    Timestamp = a.CreatedAt
                })
                .ToListAsync();
            return events.Cast<object>().ToList();
        }
    }
}
