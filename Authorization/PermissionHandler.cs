using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Clienta.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Authorization;

public class PermissionHandler 
    : AuthorizationHandler<PermissionRequirement>
{
    private readonly AppDbContext _context;
    private readonly ILogger<PermissionHandler> _logger;

    public PermissionHandler(AppDbContext context, ILogger<PermissionHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        _logger.LogInformation("PermissionHandler checking permission '{Permission}' for user '{UserId}'", 
            requirement.Permission, userId);

        if (userId == null)
        {
            _logger.LogWarning("No user ID found in claims");
            return;
        }

        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Permissions)
                .ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.Id.ToString() == userId);

        if (user == null)
        {
            _logger.LogWarning("User '{UserId}' not found in database", userId);
            return;
        }

        _logger.LogInformation("User '{UserId}' has role '{Role}'", userId, user.Role);

        // Admin bypass - case insensitive comparison
        if (user.Role?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation("Admin bypass activated for user '{UserId}'", userId);
            context.Succeed(requirement);
            return;
        }

        var hasPermission = user.Permissions
            .Any(p => p.Permission.Key == requirement.Permission);

        _logger.LogInformation("User '{UserId}' has permission '{Permission}': {HasPermission}", 
            userId, requirement.Permission, hasPermission);

        if (hasPermission)
            context.Succeed(requirement);
    }
}
