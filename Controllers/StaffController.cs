using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Entities;
using Clienta.Api.DTOs;

namespace Clienta.Api.Controllers;

[Authorize(Policy = "manage_staff")]
[ApiController]
[Route("api/staff")]
public class StaffController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;

    public StaffController(AppDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    // GET /api/staff
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var staff = await _context.BusinessUsers
            .Where(bu => bu.TenantId == _tenant.TenantId)
            .Select(bu => bu.User)
            .Where(u => u.Role == "Staff")
            .Select(u => new StaffResponse(
                u.Id,
                u.Email,
                u.FullName ?? string.Empty,
                u.RoleLabel ?? string.Empty,
                u.IsActive,
                u.Permissions.Select(p => p.Permission.Key).ToList()
            ))
            .ToListAsync();

        return Ok(staff);
    }

    // POST /api/staff
    [HttpPost]
    public async Task<IActionResult> Create(CreateStaffRequest request)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            return BadRequest("Email already exists.");

        var hashed = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = hashed,
            Role = "Staff",
            RoleLabel = request.RoleLabel,
            FullName = request.FullName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            TenantId = _tenant.TenantId
        };

        _context.Users.Add(user);

        _context.BusinessUsers.Add(new BusinessUser
        {
            TenantId = _tenant.TenantId,
            UserId = user.Id
        });

        // Assign permissions (GLOBAL permissions)
        if (request.Permissions?.Any() == true)
        {
            var permissions = await _context.Permissions
                .Where(p => request.Permissions.Contains(p.Key))
                .ToListAsync();

            foreach (var permission in permissions)
            {
                _context.UserPermissions.Add(new UserPermission
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    PermissionId = permission.Id
                });
            }
        }

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new StaffResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.RoleLabel,
            user.IsActive,
            request.Permissions ?? new List<string>()
        ));
    }

    // PUT /api/staff/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateStaffRequest request)
    {
        var user = await _context.Users
            .Include(u => u.Permissions)
            .FirstOrDefaultAsync(u => u.Id == id && u.Role == "Staff");

        if (user == null)
            return NotFound();

        user.FullName = request.FullName;
        user.RoleLabel = request.RoleLabel;
        user.IsActive = request.IsActive;

        // Remove old permissions
        var existingPermissions = await _context.UserPermissions
            .Where(up => up.UserId == user.Id)
            .ToListAsync();

        _context.UserPermissions.RemoveRange(existingPermissions);

        // Assign new permissions
        if (request.Permissions?.Any() == true)
        {
            var permissions = await _context.Permissions
                .Where(p => request.Permissions.Contains(p.Key))
                .ToListAsync();

            foreach (var permission in permissions)
            {
                _context.UserPermissions.Add(new UserPermission
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    PermissionId = permission.Id
                });
            }
        }

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // DELETE /api/staff/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.Role == "Staff");

        if (user == null)
            return NotFound();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}