using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;

namespace Clienta.Api.Services;

public class TenantService
{
    private readonly ITenantContext _tenantContext;
    private readonly AppDbContext _context;

    public TenantService(ITenantContext tenantContext, AppDbContext context)
    {
        _tenantContext = tenantContext;
        _context = context;
    }

    public Guid GetTenantId()
    {
        return _tenantContext.TenantId;
    }

    public Guid? GetCurrentUserId()
    {
        return _tenantContext.UserId;
    }
}
