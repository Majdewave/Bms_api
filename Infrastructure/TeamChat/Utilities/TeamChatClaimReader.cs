using System.Security.Claims;

namespace Clienta.Api.Infrastructure.TeamChat.Utilities;

public static class TeamChatClaimReader
{
    public static bool TryReadTenantId(ClaimsPrincipal? user, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        var raw = user?.FindFirst("tenant_id")?.Value ?? user?.FindFirst("tenantId")?.Value;
        return Guid.TryParse(raw, out tenantId);
    }

    public static bool TryReadUserId(ClaimsPrincipal? user, out Guid userId)
    {
        userId = Guid.Empty;
        var raw = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.FindFirst("sub")?.Value;

        return Guid.TryParse(raw, out userId);
    }

    public static string ReadDisplayName(ClaimsPrincipal? user)
    {
        return user?.FindFirst(ClaimTypes.Name)?.Value
            ?? user?.FindFirst("name")?.Value
            ?? user?.FindFirst("fullName")?.Value
            ?? user?.FindFirst(ClaimTypes.Email)?.Value
            ?? "Unknown User";
    }
}
