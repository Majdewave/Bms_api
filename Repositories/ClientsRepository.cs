using Clienta.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Repositories
{
    public class ClientsRepository
    {
        private readonly AppDbContext _db;
        public ClientsRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<object>> GetRecentEvents()
        {
            var events = await _db.Clients
                .OrderByDescending(c => c.CreatedAt)
                .Take(10)
                .Select(c => new
                {
                    Id = c.Id,
                    EventType = "client_created",
                    Title = "Client created",
                    ClientName = c.FullName,
                    Timestamp = c.CreatedAt
                })
                .ToListAsync();
            return events.Cast<object>().ToList();
        }
    }
}
