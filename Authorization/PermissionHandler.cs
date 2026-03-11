using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Clienta.Api.Data;

namespace Clienta.Api.Authorization;

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly AppDbContext _context;

    public PermissionHandler(AppDbContext context)
    {
        _context = context;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userId =
            context.User.FindFirst("sub")?.Value ??
            context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
            return;

        if (!Guid.TryParse(userId, out var userGuid))
            return;

        // Load all user permissions
        var permissions = await _context.UserPermissions
            .Include(up => up.Permission)
            .Where(up => up.UserId == userGuid)
            .Select(up => up.Permission.Key)
            .ToListAsync();

        // direct permission
        if (permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
            return;
        }

        // manage_* also allows view_*
        if (requirement.Permission.StartsWith("view_"))
        {
            var managePermission = requirement.Permission.Replace("view_", "manage_");

            if (permissions.Contains(managePermission))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}