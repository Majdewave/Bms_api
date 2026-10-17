using Clienta.Api.DTOs;
using Clienta.Api.Models;

namespace Clienta.Api.Services;

/// <summary>Resolves the trial duration from the explicit product discriminator; null/unknown remains Legacy.</summary>
public static class TrialDurationResolver
{
    public static int ResolveDays(TenantProductType? productType, PlatformSettingsDto settings) => productType switch
    {
        TenantProductType.Clinic => settings.ClinicTrialDays,
        TenantProductType.Business => settings.BusinessTrialDays,
        _ => settings.DefaultTrialDays,
    };
}
