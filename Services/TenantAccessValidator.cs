using Clienta.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface ITenantAccessValidator
{
    Task<bool> IsTenantAllowedAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed class TenantAccessValidator : ITenantAccessValidator
{
    private readonly MasterDbContext _masterDb;

    public TenantAccessValidator(MasterDbContext masterDb)
    {
        _masterDb = masterDb;
    }

    public Task<bool> IsTenantAllowedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return _masterDb.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => !t.IsSuspended)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
