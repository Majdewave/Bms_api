using Clienta.Api.Data;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Middleware;

public class TrialCheckMiddleware
{
    private readonly RequestDelegate _next;

    public TrialCheckMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        AppDbContext db)
    {
        // Skip check for public endpoints
        var path = context.Request.Path.Value?.ToLower();
        var method = context.Request.Method.ToUpper();
        
        if (path != null && (
            path.StartsWith("/api/onboarding") ||
            path.StartsWith("/api/auth/login") ||
            path.StartsWith("/api/dashboard") ||
            path.StartsWith("/api/billing") ||
            path.StartsWith("/health") ||
            path.StartsWith("/swagger")))
        {
            await _next(context);
            return;
        }

        // Skip if not authenticated
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        try
        {
            var tenantId = tenantContext.TenantId;
            if (tenantId == Guid.Empty)
            {
                await _next(context);
                return;
            }

            var tenant = await db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "Tenant not found" });
                return;
            }

            // Check if trial expired (but allow read-only access)
            if (tenant.Plan == PlanType.Trial && tenant.TrialEndsAt.HasValue && tenant.TrialEndsAt < DateTime.UtcNow)
            {
                // Allow GET requests (read-only)
                if (method == "GET")
                {
                    context.Response.Headers.Append("X-Trial-Expired", "true");
                    await _next(context);
                    return;
                }

                // Block POST, PUT, DELETE (write operations)
                context.Response.StatusCode = 402; // Payment Required
                await context.Response.WriteAsJsonAsync(new 
                { 
                    error = "Trial expired. Upgrade to continue creating content.",
                    message = "You can still view your data, but creating new content requires an upgrade.",
                    redirectTo = "/billing/upgrade",
                    trialExpired = true
                });
                return;
            }

            // Check if tenant is suspended (hard suspension - no access at all)
            if (tenant.IsSuspended)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new 
                { 
                    error = "Account is suspended. Please contact support.",
                    suspended = true
                });
                return;
            }

            await _next(context);
        }
        catch
        {
            // If something goes wrong, allow request to continue
            await _next(context);
        }
    }
}
