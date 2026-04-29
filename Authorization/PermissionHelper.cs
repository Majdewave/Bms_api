using System.Security.Claims;

namespace Clienta.Api.Authorization;

public static class PermissionHelper
{
    /// <summary>
    /// Returns true if the user is an Admin (full access) or has the specific permission claim.
    /// </summary>
    public static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        if (user.IsInRole("Admin"))
            return true;

        return user.Claims.Any(c => c.Type == "permission" && c.Value == permission);
    }
}
