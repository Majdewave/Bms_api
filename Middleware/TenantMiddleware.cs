using System.Security.Claims;
using Clienta.Api.Services;

namespace Clienta.Api.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var tenantClaim = context.User.FindFirst("tenant_id")?.Value;
        var userClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (Guid.TryParse(tenantClaim, out var tenantId))
        {
            tenantContext.SetTenant(tenantId);
        }

        if (Guid.TryParse(userClaim, out var userId))
        {
            tenantContext.SetUserId(userId);
        }

        await _next(context);
    }
}