namespace Clienta.Api.DTOs;

public record DepartmentFeatureItemResponse(
    string FeatureKey,
    bool TenantEnabled,
    bool DepartmentEnabled,
    bool EffectiveEnabled
);

public record UpdateDepartmentFeaturesRequest(
    List<DepartmentFeatureUpdateItem> Features
);

public record DepartmentFeatureUpdateItem(
    string FeatureKey,
    bool IsEnabled
);
