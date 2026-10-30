using Microsoft.AspNetCore.Authorization;

namespace Clienta.Api.Authorization;

public static class IntegrationAuthorization
{
    public const string ManagePolicy = "manage_integrations";

    // Tenant Admin only; delegated permissions and staff roles are deliberately not enough.
    public static void AddPolicies(AuthorizationOptions options) =>
        options.AddPolicy(ManagePolicy, policy => policy.RequireRole("Admin"));
}
