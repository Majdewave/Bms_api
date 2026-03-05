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
        // CREATE PERMISSIONS IF MISSING
        // ===============================

        var permissions = new[]
        {
            "manage_clients",
            "manage_appointments",
            "manage_notes",
            "manage_files",
            "manage_staff"
        };

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

        // ===============================
        // ASSIGN ADMIN PERMISSIONS
        // ===============================

        foreach (var permission in allPermissions)
        {
            var exists = await _db.UserPermissions.AnyAsync(up =>
                up.UserId == admin.Id &&
                up.PermissionId == permission.Id);

            if (!exists)
            {
                _db.UserPermissions.Add(new UserPermission
                {
                    Id = Guid.NewGuid(),
                    UserId = admin.Id,
                    PermissionId = permission.Id
                });
            }
        }

        // ===============================
        // ASSIGN STAFF PERMISSIONS
        // ===============================

        var staffPermissions = allPermissions
            .Where(p =>
                p.Key == "manage_clients" ||
                p.Key == "manage_appointments" ||
                p.Key == "manage_notes")
            .ToList();

        foreach (var permission in staffPermissions)
        {
            var exists = await _db.UserPermissions.AnyAsync(up =>
                up.UserId == staff.Id &&
                up.PermissionId == permission.Id);

            if (!exists)
            {
                _db.UserPermissions.Add(new UserPermission
                {
                    Id = Guid.NewGuid(),
                    UserId = staff.Id,
                    PermissionId = permission.Id
                });
            }
        }

        await _db.SaveChangesAsync();
    }
}