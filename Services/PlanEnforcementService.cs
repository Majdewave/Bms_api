using Clienta.Api.Data;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

/// <summary>
/// Central service for enforcing plan limits
/// All plan-based restrictions go through here
/// </summary>
public interface IPlanEnforcementService
{
    Task EnsureUserLimitAsync(Guid tenantId);
    Task EnsureMessageLimitAsync(Guid tenantId);
    Task<bool> CanCreateUserAsync(Guid tenantId);
    Task<bool> CanSendMessageAsync(Guid tenantId);
}

public class PlanEnforcementService : IPlanEnforcementService
{
    private readonly AppDbContext _db;
    private readonly IPlanProvider _planProvider;

    public PlanEnforcementService(AppDbContext db, IPlanProvider planProvider)
    {
        _db = db;
        _planProvider = planProvider;
    }

    /// <summary>
    /// Check if tenant can create a new user
    /// Throws exception if limit exceeded
    /// </summary>
    public async Task EnsureUserLimitAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants
            .Include(t => t.Users)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
            throw new InvalidOperationException("Tenant not found");

        var planDef = _planProvider.GetPlan(tenant.Plan);

        // -1 means unlimited
        if (planDef.UserLimit == -1)
            return;

        var currentUserCount = tenant.Users.Count;

        if (currentUserCount >= planDef.UserLimit)
        {
            throw new InvalidOperationException(
                $"User limit reached. Your {tenant.Plan} plan allows up to {planDef.UserLimit} users. " +
                $"Upgrade to add more users."
            );
        }
    }

    /// <summary>
    /// Check if tenant can send a message
    /// Throws exception if limit exceeded
    /// </summary>
    public async Task EnsureMessageLimitAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);

        if (tenant == null)
            throw new InvalidOperationException("Tenant not found");

        var planDef = _planProvider.GetPlan(tenant.Plan);

        // -1 means unlimited
        if (planDef.MessageLimit == -1)
            return;

        // Count notes (messages) sent this month
        var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var messageCount = await _db.Notes
            .Where(n => n.TenantId == tenantId && n.CreatedAt >= startOfMonth)
            .CountAsync();

        // Also count appointments as messages
        var appointmentCount = await _db.Appointments
            .Where(a => a.TenantId == tenantId && a.CreatedAt >= startOfMonth)
            .CountAsync();

        var totalCount = messageCount + appointmentCount;

        if (totalCount >= planDef.MessageLimit)
        {
            throw new InvalidOperationException(
                $"Message limit reached. Your {tenant.Plan} plan allows up to {planDef.MessageLimit} messages per month. " +
                $"Upgrade to send more messages."
            );
        }
    }

    /// <summary>
    /// Non-throwing version - returns true/false
    /// </summary>
    public async Task<bool> CanCreateUserAsync(Guid tenantId)
    {
        try
        {
            await EnsureUserLimitAsync(tenantId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Non-throwing version - returns true/false
    /// </summary>
    public async Task<bool> CanSendMessageAsync(Guid tenantId)
    {
        try
        {
            await EnsureMessageLimitAsync(tenantId);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
