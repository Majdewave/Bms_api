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
    private readonly IWebHostEnvironment _env;
    private readonly IFileStorage _fileStorage;
    private readonly IAuthService _authService;

    public StaffController(
        AppDbContext context,
        ITenantContext tenant,
        IWebHostEnvironment env,
        IFileStorage fileStorage,
        IAuthService authService)
    {
        _context = context;
        _tenant = tenant;
        _env = env;
        _fileStorage = fileStorage;
        _authService = authService;
    }

    private async Task<List<Guid>> GetDepartmentIdsAsync(Guid staffId)
    {
        return await _context.StaffDepartments
            .Where(sd => sd.StaffId == staffId)
            .OrderBy(sd => sd.CreatedAt)
            .Select(sd => sd.DepartmentId)
            .ToListAsync();
    }

    private static string? NormalizeRole(string? role) => role?.Trim() switch
    {
        "Admin" => "Admin",
        "Staff" => "Staff",
        "Interpreter" => "Interpreter",
        _ => null
    };

    private async Task SyncDepartmentsAsync(Guid staffId, IEnumerable<Guid>? departmentIds, bool requiresDepartment)
    {
        var normalizedDepartmentIds = departmentIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (requiresDepartment && normalizedDepartmentIds.Count == 0)
        {
            throw new InvalidOperationException("At least one department must be selected for non-admin staff.");
        }

        var existingLinks = await _context.StaffDepartments
            .Where(sd => sd.StaffId == staffId)
            .ToListAsync();

        _context.StaffDepartments.RemoveRange(existingLinks);

        if (normalizedDepartmentIds.Count == 0)
        {
            return;
        }

        var validDepartmentIds = await _context.Departments
            .Where(department => normalizedDepartmentIds.Contains(department.Id))
            .Select(department => department.Id)
            .ToListAsync();

        if (validDepartmentIds.Count != normalizedDepartmentIds.Count)
        {
            throw new InvalidOperationException("One or more selected departments are invalid.");
        }

        foreach (var departmentId in validDepartmentIds)
        {
            _context.StaffDepartments.Add(new StaffDepartment
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                StaffId = staffId,
                DepartmentId = departmentId,
            });
        }
    }

    private async Task<StaffResponse> BuildResponseAsync(BusinessUser businessUser)
    {
        var user = businessUser.User;
        var permissionKeys = await _context.UserPermissions
            .Where(up => up.UserId == user.Id)
            .Select(up => up.Permission.Key)
            .ToListAsync();
        var departmentIds = await GetDepartmentIdsAsync(businessUser.Id);

        return new StaffResponse(
            businessUser.Id,
            user.Id,
            user.Email,
            user.FullName ?? string.Empty,
            user.RoleLabel ?? string.Empty,
            user.Role,
            user.IsActive,
            permissionKeys,
            departmentIds,
            user.LastLoginAt,
            user.StampUrl,
            user.UseStamp,
            await IsOwnerAsync(user.Id)
        );
    }

    private Task<bool> IsOwnerAsync(Guid userId) => _context.Tenants
        .AnyAsync(tenant => tenant.Id == _tenant.TenantId && tenant.OwnerUserId == userId);

    private static StaffResponse BuildResponse(User user, Guid id, IReadOnlyList<string> permissions, IReadOnlyList<Guid> departmentIds, bool isOwner) => new(
        id,
        user.Id,
        user.Email,
        user.FullName ?? string.Empty,
        user.RoleLabel ?? string.Empty,
        user.Role,
        user.IsActive,
        permissions.ToList(),
        departmentIds.ToList(),
        user.LastLoginAt,
        user.StampUrl,
        user.UseStamp,
        isOwner);

    // GET /api/staff
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var ownerUserId = await _context.Tenants
            .Where(tenant => tenant.Id == _tenant.TenantId)
            .Select(tenant => tenant.OwnerUserId)
            .FirstOrDefaultAsync();

        var staff = await _context.BusinessUsers
            .Include(bu => bu.User)
            .Where(bu => bu.TenantId == _tenant.TenantId)
            .ToListAsync();

        var staffIds = staff.Select(member => member.Id).ToList();
        var userIds = staff.Select(member => member.UserId).ToList();
        var permissionRows = await _context.UserPermissions
            .Where(up => userIds.Contains(up.UserId))
            .Select(up => new { up.UserId, PermissionKey = up.Permission.Key })
            .ToListAsync();

        var permissionMap = permissionRows
            .GroupBy(row => row.UserId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.PermissionKey).ToList());

        var departmentMap = await _context.StaffDepartments
            .Where(sd => staffIds.Contains(sd.StaffId))
            .GroupBy(sd => sd.StaffId)
            .ToDictionaryAsync(group => group.Key, group => group.Select(item => item.DepartmentId).OrderBy(id => id).ToList());

        var staffResponse = staff.Select(bu => BuildResponse(
            bu.User,
            bu.Id,
            permissionMap.TryGetValue(bu.UserId, out var keys) ? keys : new List<string>(),
            departmentMap.TryGetValue(bu.Id, out var ids) ? ids : new List<Guid>(),
            bu.User.Id == ownerUserId
        )).ToList();

        if (ownerUserId.HasValue && staff.All(member => member.UserId != ownerUserId.Value))
        {
            var owner = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Id == ownerUserId.Value && user.TenantId == _tenant.TenantId);

            if (owner != null)
            {
                staffResponse.Add(BuildResponse(owner, owner.Id, [], [], true));
            }
        }

        return Ok(staffResponse);
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
        {
            var owner = await _context.Users.FirstOrDefaultAsync(user =>
                user.Id == id &&
                user.TenantId == _tenant.TenantId &&
                _context.Tenants.Any(tenant => tenant.Id == _tenant.TenantId && tenant.OwnerUserId == user.Id));

            return owner == null ? NotFound() : Ok(BuildResponse(owner, owner.Id, [], [], true));
        }

        return Ok(await BuildResponseAsync(businessUser));
    }

    // POST /api/staff
    [HttpPost]
    public async Task<IActionResult> Create(CreateStaffRequest request)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            return BadRequest("Email already exists.");

        var role = NormalizeRole(request.Role);
        if (role == null)
            return BadRequest("Role must be Admin, Staff, or Interpreter.");

        var permissions = role == "Staff" ? request.Permissions : [];
        var departmentIds = role == "Interpreter" ? [] : request.DepartmentIds;

        if (role != "Interpreter" && string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Password is required for Admin and Staff users.");

        var hashed = role == "Interpreter"
            ? string.Empty
            : BCrypt.Net.BCrypt.HashPassword(request.Password!);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = hashed,
            Role = role,
            RoleLabel = request.RoleLabel,
            FullName = request.FullName,
            UseStamp = request.UseStamp,
            IsActive = role != "Interpreter",
            CreatedAt = DateTime.UtcNow,
            TenantId = _tenant.TenantId
        };

        _context.Users.Add(user);

        _context.BusinessUsers.Add(new BusinessUser
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            UserId = user.Id
        });

        // Assign permissions only for Staff users
        if (role == "Staff" && permissions.Any())
        {
            var allowedPermissions = await _context.Permissions
            .Where(p => permissions.Contains(p.Key))
                .ToListAsync();

            foreach (var permission in allowedPermissions)
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
        if (businessUser == null)
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to create staff.");

        try
        {
            await SyncDepartmentsAsync(businessUser.Id, departmentIds, role == "Staff");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        await _context.SaveChangesAsync();

        if (role == "Interpreter")
        {
            var inviteSent = await _authService.SendInviteToExistingUserAsync(
                user.Id,
                _tenant.TenantId);

            if (!inviteSent)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "Interpreter was created, but the invitation could not be sent.");
            }
        }
        return CreatedAtAction(nameof(GetAll), await BuildResponseAsync(businessUser));
    }

    // PUT /api/staff/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateStaffRequest request)
    {
        var ownerUserId = await _context.Tenants
            .Where(tenant => tenant.Id == _tenant.TenantId)
            .Select(tenant => tenant.OwnerUserId)
            .FirstOrDefaultAsync();

        var businessUser = await _context.BusinessUsers
            .Include(bu => bu.User)
            .ThenInclude(u => u.Permissions)
            .FirstOrDefaultAsync(bu => (bu.Id == id || bu.UserId == id) && bu.TenantId == _tenant.TenantId);

        var user = businessUser?.User;
        if (user == null && ownerUserId == id)
        {
            user = await _context.Users.FirstOrDefaultAsync(candidate =>
                candidate.Id == id && candidate.TenantId == _tenant.TenantId);
        }

        if (user == null)
            return NotFound();

        var role = NormalizeRole(request.Role);
        if (role == null)
            return BadRequest("Role must be Admin, Staff, or Interpreter.");

        var isOwner = ownerUserId == user.Id;
        if (isOwner && (!request.IsActive || role != "Admin"))
            return Conflict("The tenant owner cannot be deactivated or changed from the Admin role.");

        if (isOwner && !string.IsNullOrWhiteSpace(request.Password) && _tenant.UserId != user.Id)
            return Forbid();

        var permissions = role == "Staff" ? request.Permissions : [];
        var departmentIds = role == "Interpreter" ? [] : request.DepartmentIds;

        user.FullName = request.FullName;
        user.Email = request.Email;
        user.RoleLabel = request.RoleLabel;
        user.IsActive = request.IsActive;
        user.UseStamp = request.UseStamp;
        user.Role = role;

        // update password
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        // Remove old permissions
        if (!isOwner)
        {
            var existingPermissions = await _context.UserPermissions
                .Where(up => up.UserId == user.Id)
                .ToListAsync();

            _context.UserPermissions.RemoveRange(existingPermissions);
        }

        // Assign permissions ONLY if Staff
        if (!isOwner && user.Role == "Staff" && permissions.Any())
        {
            var allowedPermissions = await _context.Permissions
            .Where(p => permissions.Contains(p.Key))
                .ToListAsync();

            foreach (var permission in allowedPermissions)
            {
                _context.UserPermissions.Add(new UserPermission
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    PermissionId = permission.Id
                });
            }
        }

        if (businessUser != null)
        {
            try
            {
                await SyncDepartmentsAsync(
                    businessUser.Id,
                    departmentIds,
                    user.Role == "Staff");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        await _context.SaveChangesAsync();

        return businessUser == null
            ? Ok(BuildResponse(user, user.Id, [], [], true))
            : Ok(await BuildResponseAsync(businessUser));
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
        var ownerUserId = await _context.Tenants
            .Where(tenant => tenant.Id == _tenant.TenantId)
            .Select(tenant => tenant.OwnerUserId)
            .FirstOrDefaultAsync();

        if (ownerUserId == id)
            return Conflict("The tenant owner cannot be deleted.");

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

        if (ownerUserId == user.Id)
            return Conflict("The tenant owner cannot be deleted.");

        var hasInterpretationHistory = await _context.InterpretationRequests.AnyAsync(request =>
            request.TenantId == _tenant.TenantId &&
            (request.AssignedInterpreterId == user.Id || request.RequestedByUserId == user.Id));

        if (hasInterpretationHistory)
            return Conflict("User cannot be deleted because they are linked to an interpretation request.");

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