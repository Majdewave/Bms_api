


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
        var tenantIdClaim = context.User.FindFirst("tenant_id")?.Value;
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;


        if (!string.IsNullOrEmpty(tenantIdClaim))
            tenantContext.SetTenant(Guid.Parse(tenantIdClaim));

        if (!string.IsNullOrEmpty(userIdClaim))
            tenantContext.SetUserId(Guid.Parse(userIdClaim));

        await _next(context);
    }
}


