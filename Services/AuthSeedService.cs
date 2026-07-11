using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public class AuthSeedService
{
    private readonly AppDbContext _db;

    public AuthSeedService(AppDbContext db)
    {
        _db = db;
    }

    public async Task SeedAsync()
    {
        // ===============================
        // REMOVE DUPLICATE DEFAULT TENANTS
        // ===============================
        var defaultTenants = await _db.Tenants.Where(t => t.Subdomain == "default").ToListAsync();
        if (defaultTenants.Count > 1)
        {
            // Keep the first, remove the rest
            var keepTenant = defaultTenants.First();
            var removeTenants = defaultTenants.Skip(1).ToList();
            _db.Tenants.RemoveRange(removeTenants);
            await _db.SaveChangesAsync();
        }
        // ===============================
        // CREATE PERMISSIONS IF MISSING
        // ===============================

        var permissions = new[]
        {
            "manage_clients",
            "manage_appointments",
            "manage_notes",
            "manage_files",
            "manage_staff",
            "manage_invoices",
            "manage_whatsapp"
        };

        // Remove legacy manage_admins permission if it exists
        var legacyAdminPermission = await _db.Permissions.FirstOrDefaultAsync(p => p.Key == "manage_admins");
        if (legacyAdminPermission != null)
        {
            var legacyUserPerms = _db.UserPermissions.Where(up => up.PermissionId == legacyAdminPermission.Id);
            _db.UserPermissions.RemoveRange(legacyUserPerms);
            _db.Permissions.Remove(legacyAdminPermission);
        }

        foreach (var key in permissions)
        {
            var exists = await _db.Permissions.AnyAsync(p => p.Key == key);

            if (!exists)
            {
                _db.Permissions.Add(new Permission
                {
                    Id = Guid.NewGuid(),
                    Key = key
                });
            }
        }

        await _db.SaveChangesAsync();

        var allPermissions = await _db.Permissions.ToListAsync();

        // ===============================
        // CREATE DEFAULT TENANT IF NONE
        // ===============================

        var tenant = await _db.Tenants.FirstOrDefaultAsync();

        tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == "default");
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

        // ===============================
        // CREATE ADMIN IF NOT EXISTS
        // ===============================

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Role == "Admin");

        if (admin == null)
        {
            admin = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@clienta.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                Role = "Admin",
                FullName = "System Admin",
                IsActive = true,
                TenantId = tenant.Id,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(admin);

            _db.BusinessUsers.Add(new BusinessUser
            {
                TenantId = tenant.Id,
                UserId = admin.Id
            });

            await _db.SaveChangesAsync();
        }

        // ===============================
        // CREATE STAFF IF NOT EXISTS
        // ===============================

        var staff = await _db.Users.FirstOrDefaultAsync(u => u.Email == "staff@clienta.local");

        if (staff == null)
        {
            staff = new User
            {
                Id = Guid.NewGuid(),
                Email = "staff@clienta.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Staff123!"),
                Role = "Staff",
                FullName = "Staff User",
                IsActive = true,
                TenantId = tenant.Id,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(staff);

            _db.BusinessUsers.Add(new BusinessUser
            {
                TenantId = tenant.Id,
                UserId = staff.Id
            });

            await _db.SaveChangesAsync();
        }

        // Admin users have full access via Role = "Admin" — no DB permissions needed.

        // ===============================
        // ASSIGN STAFF PERMISSIONS
        // ===============================

        var staffPermissions = allPermissions
            .Where(p =>
                p.Key == "manage_clients" ||
                p.Key == "manage_appointments" ||
                p.Key == "manage_notes")
            .ToList();

        var staffUsers = await _db.Users
            .Where(u => u.Role == "Staff")
            .ToListAsync();

        foreach (var staffUser in staffUsers)
        {
            foreach (var permission in staffPermissions)
            {
                var exists = await _db.UserPermissions.AnyAsync(up =>
                    up.UserId == staffUser.Id &&
                    up.PermissionId == permission.Id);

                if (!exists)
                {
                    _db.UserPermissions.Add(new UserPermission
                    {
                        Id = Guid.NewGuid(),
                        UserId = staffUser.Id,
                        PermissionId = permission.Id
                    });
                }
            }
        }

        await _db.SaveChangesAsync();
    }
}