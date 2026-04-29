using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Clienta.Api.Services;

namespace Clienta.Api.Middleware
{
    public class TenantMiddleware
    {
        private readonly RequestDelegate _next;

        public TenantMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, ITenantContext tenantContext)
        {
            var tenantIdClaim = context.User?.Claims?
                .FirstOrDefault(c => c.Type == "tenant_id");

            if (tenantIdClaim != null && Guid.TryParse(tenantIdClaim.Value, out var tenantId))
            {
                tenantContext.SetTenant(tenantId);
            }

           
            var userIdClaim = context.User?.FindFirst(ClaimTypes.NameIdentifier);
            

            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                tenantContext.SetUserId(userId);
            }

            await _next(context);
        }
    }
}