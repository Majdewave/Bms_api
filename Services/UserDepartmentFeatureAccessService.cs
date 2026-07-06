using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IUserDepartmentFeatureAccessService
{
    Task<bool> IsFeatureEnabledAsync(Guid? departmentId, string featureKey);
    Task<bool> CanCurrentUserAccessFeatureAsync(string featureKey);
    Task<bool> CanUserAccessFeatureAsync(Guid tenantId, Guid userId, string featureKey);
    Task<EffectiveDepartmentFeaturesResponse> GetCurrentUserEffectiveFeaturesAsync();
}

public class UserDepartmentFeatureAccessService : IUserDepartmentFeatureAccessService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IDepartmentFeatureResolver _departmentFeatureResolver;

    private static readonly string[] FirstConsumerFeatureKeys =
    [
        "prescriptionsEnabled",
        "drugsEnabled",
        "consentFormsEnabled",
        "visitSummariesEnabled",
        "beforeAfterPhotosEnabled",
        "teamChatEnabled"
    ];

    public UserDepartmentFeatureAccessService(
        AppDbContext db,
        ITenantContext tenantContext,
        IDepartmentFeatureResolver departmentFeatureResolver)
    {
        _db = db;
        _tenantContext = tenantContext;
        _departmentFeatureResolver = departmentFeatureResolver;
    }

    public async Task<bool> CanCurrentUserAccessFeatureAsync(string featureKey)
    {
        return await IsFeatureEnabledAsync(null, featureKey);
    }

    public async Task<bool> IsFeatureEnabledAsync(Guid? departmentId, string featureKey)
    {
        var tenantId = _tenantContext.TenantId;
        var userId = _tenantContext.UserId;

        if (tenantId == Guid.Empty || userId is null || userId.Value == Guid.Empty)
        {
            return false;
        }

        if (departmentId.HasValue)
        {
            return await _departmentFeatureResolver.IsFeatureEnabledAsync(tenantId, departmentId.Value, featureKey);
        }

        return await CanUserAccessFeatureAsync(tenantId, userId.Value, featureKey);
    }

    public async Task<bool> CanUserAccessFeatureAsync(Guid tenantId, Guid userId, string featureKey)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || string.IsNullOrWhiteSpace(featureKey))
        {
            return false;
        }

        var departmentIds = await _db.StaffDepartments
            .AsNoTracking()
            .Where(sd => sd.TenantId == tenantId && sd.Staff.UserId == userId)
            .Select(sd => sd.DepartmentId)
            .Distinct()
            .ToListAsync();

        if (departmentIds.Count == 0)
        {
            return await _departmentFeatureResolver.IsFeatureEnabledAsync(tenantId, null, featureKey);
        }

        foreach (var departmentId in departmentIds)
        {
            var isEnabled = await _departmentFeatureResolver.IsFeatureEnabledAsync(tenantId, departmentId, featureKey);
            if (isEnabled)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<EffectiveDepartmentFeaturesResponse> GetCurrentUserEffectiveFeaturesAsync()
    {
        var tenantId = _tenantContext.TenantId;
        var userId = _tenantContext.UserId;

        if (tenantId == Guid.Empty || userId is null || userId.Value == Guid.Empty)
        {
            return new EffectiveDepartmentFeaturesResponse(
                false,
                false,
                false,
                false,
                false,
                false
            );
        }

        var results = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in FirstConsumerFeatureKeys)
        {
            results[key] = await CanUserAccessFeatureAsync(tenantId, userId.Value, key);
        }

        return new EffectiveDepartmentFeaturesResponse(
            results["prescriptionsEnabled"],
            results["drugsEnabled"],
            results["consentFormsEnabled"],
            results["visitSummariesEnabled"],
            results["beforeAfterPhotosEnabled"],
            results["teamChatEnabled"]
        );
    }
}