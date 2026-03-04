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
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
            return;

        var hasPermission = await _context.UserPermissions
            .Include(up => up.Permission)
            .AnyAsync(up =>
                up.UserId == Guid.Parse(userId) &&
                up.Permission.Key == requirement.Permission);

        if (hasPermission)
            context.Succeed(requirement);
    }
}