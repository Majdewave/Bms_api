using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Entities;
using Clienta.Api.DTOs;

namespace Clienta.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/staff")]
public class StaffController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IWebHostEnvironment _env;

    public StaffController(AppDbContext context, ITenantContext tenant, IWebHostEnvironment env)
    {
        _context = context;
        _tenant = tenant;
        _env = env;
    }

    // GET /api/staff
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var staff = await _context.BusinessUsers
            .Include(bu => bu.User)
            .Where(bu => bu.TenantId == _tenant.TenantId)
            .Select(bu => new StaffResponse(
                bu.Id, // BusinessUserId
                bu.User.Id, // UserId
                bu.User.Email,
                bu.User.FullName ?? string.Empty,
                bu.User.RoleLabel ?? string.Empty,
                bu.User.Role,
                bu.User.IsActive,
                bu.User.Permissions.Select(p => p.Permission.Key).ToList(),
                bu.User.StampUrl,
                bu.User.UseStamp
            ))
            .ToListAsync();

        return Ok(staff);
    }

    // GET /api/staff/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        // Find BusinessUser by Id and TenantId
        var businessUser = await _context.BusinessUsers
            .Include(bu => bu.User)
            .ThenInclude(u => u.Permissions)
            .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(bu =>(bu.Id == id || bu.UserId == id) && bu.TenantId == _tenant.TenantId);
      
        if (businessUser == null)
            return NotFound();

        var user = businessUser.User;
        var response = new StaffResponse(
            businessUser.Id, // BusinessUserId
            user.Id, // UserId
            user.Email,
            user.FullName ?? string.Empty,
            user.RoleLabel ?? string.Empty,
            user.Role,
            user.IsActive,
            user.Permissions.Select(p => p.Permission.Key).ToList(),
            user.StampUrl,
            user.UseStamp
        );

        return Ok(response);
    }

    // POST /api/staff
    [HttpPost]
    public async Task<IActionResult> Create(CreateStaffRequest request)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            return BadRequest("Email already exists.");

        var hashed = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var role = request.Role == "Admin" ? "Admin" : "Staff";

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = hashed,
            Role = role,
            RoleLabel = request.RoleLabel,
            FullName = request.FullName,
            UseStamp = request.UseStamp,
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

        // Assign permissions only for Staff users
        if (role == "Staff" && request.Permissions?.Any() == true)
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
        var businessUser = await _context.BusinessUsers.FirstOrDefaultAsync(bu => bu.UserId == user.Id && bu.TenantId == _tenant.TenantId);
        return CreatedAtAction(nameof(GetAll), new StaffResponse(
            businessUser?.Id ?? Guid.Empty, // BusinessUserId
            user.Id, // UserId
            user.Email,
            user.FullName ?? string.Empty,
            user.RoleLabel ?? string.Empty,
            user.Role,
            user.IsActive,
            role == "Staff" ? request.Permissions ?? new List<string>() : new List<string>(),
            user.StampUrl,
            user.UseStamp
        ));
    }

    // PUT /api/staff/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateStaffRequest request)
    {
        // Find BusinessUser by Id and TenantId
        var businessUser = await _context.BusinessUsers
            .Include(bu => bu.User)
            .ThenInclude(u => u.Permissions)
            .FirstOrDefaultAsync(bu => bu.Id == id && bu.TenantId == _tenant.TenantId);

        if (businessUser == null)
            return NotFound();

        var user = businessUser.User;

        user.FullName = request.FullName;
        user.RoleLabel = request.RoleLabel;
        user.IsActive = request.IsActive;
        user.UseStamp = request.UseStamp;

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

    // POST /api/staff/{id}/stamp
    [HttpPost("{id}/stamp")]
    public async Task<IActionResult> UploadStamp(Guid id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedMimeTypes.Contains(file.ContentType?.ToLower()))
            return BadRequest("Only image files are allowed (JPEG, PNG, GIF, WebP)");

        const long maxFileSize = 2 * 1024 * 1024;
        if (file.Length > maxFileSize)
            return BadRequest("File size must not exceed 2MB");

        // Find BusinessUser by id (not User)
        var businessUser = await _context.BusinessUsers
            .Include(bu => bu.User)
            .FirstOrDefaultAsync(bu => bu.Id == id && bu.TenantId == _tenant.TenantId);

        if (businessUser == null)
            return NotFound("Staff not found");

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "tenants", _tenant.TenantId.ToString(), "staff", id.ToString());
        Directory.CreateDirectory(uploadsDir);

        var fileExtension = Path.GetExtension(file.FileName).ToLower();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var fileName = $"stamp_{id}_{timestamp}{fileExtension}";
        var filePath = Path.Combine(uploadsDir, fileName);

        foreach (var existingFile in Directory.GetFiles(uploadsDir, "stamp_*"))
        {
            System.IO.File.Delete(existingFile);
        }

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Set stamp info on BusinessUser
        businessUser.User.StampUrl = $"/uploads/tenants/{_tenant.TenantId}/staff/{id}/{fileName}";
        businessUser.User.UseStamp = true;

        await _context.SaveChangesAsync();

        return Ok(new { stampUrl = businessUser.User.StampUrl, useStamp = businessUser.User.UseStamp });
    }

    // DELETE /api/staff/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        // 1. Log current tenant
        var currentTenant = _tenant.TenantId;
        Console.WriteLine($"[DELETE Staff] Current TenantId: {currentTenant}");

        // 2. Find business user with IgnoreQueryFilters for debugging
        var businessUser = await _context.BusinessUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(bu => bu.Id == id);

        if (businessUser == null)
        {
            Console.WriteLine($"[DELETE Staff] BusinessUser not found for Id: {id}");
            return NotFound();
        }

        // 2. Compare with DB TenantId
        Console.WriteLine($"[DELETE Staff] BusinessUser.TenantId: {businessUser.TenantId}");
        if (businessUser.TenantId != currentTenant)
        {
            Console.WriteLine($"[DELETE Staff] TenantId mismatch! Request: {currentTenant}, DB: {businessUser.TenantId}");
            return Forbid();
        }

        // 3. Find user
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == businessUser.UserId);
        if (user == null)
        {
            Console.WriteLine($"[DELETE Staff] User not found for BusinessUser.UserId: {businessUser.UserId}");
            return NotFound();
        }

        // Remove related permissions
        var userPermissions = await _context.UserPermissions
            .Where(up => up.UserId == user.Id)
            .ToListAsync();
        _context.UserPermissions.RemoveRange(userPermissions);

        // Remove business user link
        _context.BusinessUsers.Remove(businessUser);

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid? performedByUserId = null;
        if (Guid.TryParse(currentUserId, out var parsedUserId))
            performedByUserId = parsedUserId;

        User? currentUser = null;
        if (performedByUserId.HasValue)
            currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == performedByUserId.Value);

        var performedByLabel = currentUser != null
            ? $"{currentUser.Role} - {currentUser.FullName}"
            : "Admin";

        var isStaff = user.Role == "Staff";

        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            UserId = performedByUserId,
            EntityName = user.Role, // Admin / Staff
            ActionType = "user_deleted",
            EntityId = user.Id.ToString(),
            NewValues = user.FullName ?? user.Email,
            PerformedBy = performedByLabel,
            CreatedAt = DateTime.UtcNow
        });

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}