using Clienta.Api.Data;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

public class SubscriptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SubscriptionMiddleware> _logger;

    public SubscriptionMiddleware(RequestDelegate next, ILogger<SubscriptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context, ITenantContext tenantContext, AppDbContext db)
    {
        // skip public routes
        var path = context.Request.Path.Value?.ToLower() ?? "";


        // allow root + static + health checks
        if (path == "/" ||
            path == "" ||
            path.Contains("index.html") ||
            path.StartsWith("/assets") ||
            path.StartsWith("/static") ||
            path.EndsWith(".js") ||
            path.EndsWith(".css") ||
            path.EndsWith(".ico") ||
            path.EndsWith(".png") ||
            path.EndsWith(".jpg")||
            context.Request.Headers["User-Agent"].ToString().Contains("ELB-HealthChecker"))
        {
            await _next(context);
            return;
        }

        if (path != null && (
            path.Contains("/login") ||
            path.Contains("/register") ||
            path.Contains("/stripe") ||
            path.Contains("/webhook") ||
            path.Contains("/reset-password") ||
            path.Contains("/billing") ||
            path.Contains("/plans")
        ))
        {
            await _next(context);
            return;
        }

        if (tenantContext == null || tenantContext.TenantId == Guid.Empty)
        {
            await _next(context);
            return;
        }

        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        var cacheKey = $"tenant_{tenantContext.TenantId}";
        var tenant = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId);
        });

        if (tenant == null)
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new {
                error = "tenant_not_found",
                message = "Tenant not found"
            }));
            return;
        }

        var now = DateTime.UtcNow;
        var trialExpired = tenant.TrialEndsAt < now;
        var hasActiveSubscription =
             tenant.SubscriptionStatus == SubscriptionStatus.Active ||
             tenant.SubscriptionStatus == SubscriptionStatus.Trialing;

        if (trialExpired && !hasActiveSubscription)
        {
            _logger.LogWarning($"Trial expired for tenant {tenant.Id}");
            context.Response.StatusCode = 402; // Payment Required
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new {
                error = "trial_expired",
                message = "Trial expired. Please upgrade your plan."
            }));
            return;
        }

        await _next(context);
    }
}
