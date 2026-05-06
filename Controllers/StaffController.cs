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
    private readonly IFileStorage _fileStorage;

    public StaffController(AppDbContext context, ITenantContext tenant, IWebHostEnvironment env, IFileStorage fileStorage)
    {
        _context = context;
        _tenant = tenant;
        _env = env;
        _fileStorage = fileStorage;
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
            //.IgnoreQueryFilters()
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
        user.Role = request.Role == "Admin" ? "Admin" : "Staff";

        // update password
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        // Remove old permissions
        var existingPermissions = await _context.UserPermissions
            .Where(up => up.UserId == user.Id)
            .ToListAsync();

        _context.UserPermissions.RemoveRange(existingPermissions);

        // Assign permissions ONLY if Staff
        if (user.Role == "Staff" && request.Permissions?.Any() == true)
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


        var fileExtension = Path.GetExtension(file.FileName).ToLower();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var fileName = $"stamp_{id}_{timestamp}{fileExtension}";
        var key = $"tenants/{_tenant.TenantId}/staff/{id}/{fileName}";

        if (!string.IsNullOrWhiteSpace(businessUser.User.StampUrl))
        {
            if (Uri.TryCreate(businessUser.User.StampUrl, UriKind.Absolute, out var oldUri))
            {
                var oldKey = oldUri.AbsolutePath.TrimStart('/');
                await _fileStorage.DeleteAsync(oldKey);
            }
        }

        using var stream = file.OpenReadStream();
        var stampUrl = await _fileStorage.UploadAsync(stream, key, file.ContentType);

        businessUser.User.StampUrl = stampUrl;
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
           // .IgnoreQueryFilters()
            .FirstOrDefaultAsync(bu => bu.Id == id && bu.TenantId == _tenant.TenantId);

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

        // 1. מחיקת appointments שנוצרו ע"י המשתמש
        var appointments = await _context.Appointments
            .Where(a => a.TenantId == _tenant.TenantId)
            .Where(a => a.CreatedByUserId == user.Id)
            .ToListAsync();

        _context.Appointments.RemoveRange(appointments);

        // 2. ניתוק StaffId (בגלל Restrict)
        var staffAppointments = await _context.Appointments
            //.IgnoreQueryFilters()
            .Where(a => a.StaffId == businessUser.Id)
            .ToListAsync();

        foreach (var a in staffAppointments)
        {
            a.StaffId = null;
        }

        // 3. מחיקת חתימה (Stamp)
        if (!string.IsNullOrEmpty(user.StampUrl))
        {
            if (Uri.TryCreate(user.StampUrl, UriKind.Absolute, out var uri))
            {
                var key = uri.AbsolutePath.TrimStart('/');
                await _fileStorage.DeleteAsync(key);
            }
        }


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

        //  4. מציאת Clients של המשתמש לפי appointments
        var clientIds = await _context.Appointments
           // .IgnoreQueryFilters()
            .Where(a => a.CreatedByUserId == user.Id)
            .Select(a => a.ClientId)
            .Distinct()
            .ToListAsync();

        //  5. מחיקת תמונות טיפולים של אותם Clients
        var photos = await _context.ClientTreatmentPhotos
          //  .IgnoreQueryFilters()
            .Where(p => clientIds.Contains(p.ClientId))
            .ToListAsync();

        foreach (var photo in photos)
        {
            if (!string.IsNullOrEmpty(photo.BeforeImageUrl))
            {
                if (Uri.TryCreate(photo.BeforeImageUrl, UriKind.Absolute, out var beforeUri))
                {
                    var key = beforeUri.AbsolutePath.TrimStart('/');
                    await _fileStorage.DeleteAsync(key);
                }
            }

            if (!string.IsNullOrEmpty(photo.AfterImageUrl))
            {
                if (Uri.TryCreate(photo.AfterImageUrl, UriKind.Absolute, out var afterUri))
                {
                    var key = afterUri.AbsolutePath.TrimStart('/');
                    await _fileStorage.DeleteAsync(key);
                }
            }
        }

        _context.ClientTreatmentPhotos.RemoveRange(photos);

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}