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
        if (await _db.Users.AnyAsync())
            return;

        var tenantId = Guid.NewGuid();

        // CREATE TENANT FIRST
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Default Tenant",
            Subdomain = "default",
            CreatedAt = DateTime.UtcNow
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        // CREATE PERMISSIONS FIRST
        var manageClients = new Permission { Id = Guid.NewGuid(), Key = "manage_clients" };
        var manageAppointments = new Permission { Id = Guid.NewGuid(), Key = "manage_appointments" };
        var manageNotes = new Permission { Id = Guid.NewGuid(), Key = "manage_notes" };
        var manageFiles = new Permission { Id = Guid.NewGuid(), Key = "manage_files" };
        var manageStaff = new Permission { Id = Guid.NewGuid(), Key = "manage_staff" };

        _db.Permissions.AddRange(
            manageClients,
            manageAppointments,
            manageNotes,
            manageFiles,
            manageStaff
        );

        // CREATE ADMIN USER
        var admin = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@clienta.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
            Role = "Admin",
            FullName = "System Admin",
            IsActive = true,
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(admin);

        // LINK USER TO TENANT
        _db.BusinessUsers.Add(new BusinessUser
        {
            TenantId = tenantId,
            UserId = admin.Id
        });

        // SAVE FIRST (IMPORTANT FOR FK)
        await _db.SaveChangesAsync();

        // ASSIGN PERMISSIONS
        _db.UserPermissions.AddRange(
            new UserPermission { Id = Guid.NewGuid(), UserId = admin.Id, PermissionId = manageClients.Id },
            new UserPermission { Id = Guid.NewGuid(), UserId = admin.Id, PermissionId = manageAppointments.Id },
            new UserPermission { Id = Guid.NewGuid(), UserId = admin.Id, PermissionId = manageNotes.Id },
            new UserPermission { Id = Guid.NewGuid(), UserId = admin.Id, PermissionId = manageFiles.Id },
            new UserPermission { Id = Guid.NewGuid(), UserId = admin.Id, PermissionId = manageStaff.Id }
        );

        await _db.SaveChangesAsync();
    }
}