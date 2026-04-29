using Clienta.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Repositories
{
    public class BusinessUsersRepository
    {
        private readonly AppDbContext _db;
        public BusinessUsersRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<object>> GetRecentEvents()
        {
            var events = await _db.BusinessUsers
                .OrderByDescending(s => s.User.CreatedAt)
                .Take(10)
                .Select(s => new
                {
                    Id = s.Id,
                    EventType = "staff_created",
                    Title = "Staff member added",
                    StaffName = s.User.FullName,
                    Timestamp = s.User.CreatedAt
                })
                .ToListAsync();
            return events.Cast<object>().ToList();
        }
    }
}
