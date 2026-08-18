namespace Clienta.Api.Authorization;

public static class PlatformAuthConstants
{
    public const string TenantScheme = "TenantBearer";
    public const string PlatformScheme = "PlatformBearer";
    public const string ImagingGatewayScheme = "ImagingGatewayKey";

    public const string PolicyOwner = "platform_owner";
    public const string PolicyAdminOrOwner = "platform_admin_or_owner";
    public const string PolicySupportOrAbove = "platform_support_or_above";
    public const string PolicyImagingGateway = "imaging_gateway";
}
