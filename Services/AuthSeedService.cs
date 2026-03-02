using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Entities;


namespace Clienta.Api.Services
{
    public class AuthSeedService
    {
        private readonly AppDbContext _db;
        public AuthSeedService(AppDbContext db)
        {
            _db = db;
        }

        public async Task SeedAsync()
        {
            // Ensure default tenant exists
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == "default");
            if (tenant == null)
            {
                tenant = new Tenant
                {
                    Id = Guid.NewGuid(),
                    Name = "Default Tenant",
                    Subdomain = "default",
                    CreatedAt = DateTime.UtcNow
                };
                _db.Tenants.Add(tenant);
                await _db.SaveChangesAsync();
            }

            // Ensure default admin user exists
            var admin = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == "admin@local.com");
            if (admin == null)
            {
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = "admin@local.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                    Role = "Admin",
                    IsActive = true,
                    TenantId = tenant.Id,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                var businessUser = new BusinessUser
                {
                    UserId = user.Id,
                    TenantId = tenant.Id
                };
                _db.BusinessUsers.Add(businessUser);
                await _db.SaveChangesAsync();
            }
        }
    }
}
