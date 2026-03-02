using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;

namespace Clienta.Api.Services;

public class TenantResolver
{
    private readonly MasterDbContext _masterDb;
    private readonly IHttpContextAccessor _http;

    public TenantResolver(MasterDbContext masterDb, IHttpContextAccessor http)
    {
        _masterDb = masterDb;
        _http = http;
    }

    public async Task<Guid> ResolveTenantIdAsync()
    {
        // 1. Try to get tenant from subdomain
        var host = _http.HttpContext?.Request.Host.Host;
        
        if (!string.IsNullOrEmpty(host))
        {
            var parts = host.Split('.');
            if (parts.Length > 1) // Has subdomain
            {
                var subdomain = parts[0];
                
                var tenantId = await _masterDb.Tenants
                    .Where(t => t.Subdomain == subdomain)
                    .Select(t => t.Id)
                    .FirstOrDefaultAsync();

                if (tenantId != Guid.Empty)
                    return tenantId;
            }
        }

        // 2. Try to get tenant from X-Tenant-Id header
        if (_http.HttpContext?.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader) == true)
        {
            if (Guid.TryParse(tenantHeader, out var tenantId))
            {
                var exists = await _masterDb.Tenants.AnyAsync(t => t.Id == tenantId);
                if (exists)
                    return tenantId;
            }
        }

        // 3. Try to get tenant from authenticated user (JWT)
        if (_http.HttpContext?.User?.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = _http.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                var tenantId = await _masterDb.BusinessUsers
                    .Where(bu => bu.UserId == userId)
                    .Select(bu => bu.TenantId)
                    .FirstOrDefaultAsync();

                if (tenantId != Guid.Empty)
                    return tenantId;
            }
        }

        throw new Exception("Tenant not found");
    }

    public string? GetSubdomain()
    {
        var host = _http.HttpContext?.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
            return null;

        var parts = host.Split('.');
        return parts.Length > 1 ? parts[0] : null;
    }
}
