using System.ComponentModel.DataAnnotations;
using Clienta.Api.Models;

namespace Clienta.Api.DTOs;

public sealed record BusinessCommercialPlanDto(
    string ProductType,
    string PlanType,
    string DisplayName,
    string? Description,
    decimal? MonthlyPrice,
    decimal? AnnualPrice,
    bool Enabled,
    bool AvailableForPurchase,
    int DisplayOrder,
    IReadOnlyList<string> Capabilities);

public sealed record UpdateBusinessPlanConfigurationRequest(
    [property: Required]
    [property: StringLength(100, MinimumLength = 1)]
    string DisplayName,
    string? Description,
    [property: Range(typeof(decimal), "0", "9999999999999999.99")]
    decimal? MonthlyPrice,
    [property: Range(typeof(decimal), "0", "9999999999999999.99")]
    decimal? AnnualPrice,
    bool Enabled,
    [property: Range(0, int.MaxValue)]
    int DisplayOrder);

public static class BusinessCommercialPlanDtoMapper
{
    public static BusinessCommercialPlanDto ToDto(Services.BusinessCommercialPlanDefinition definition)
    {
        var configuration = definition.Configuration;
        return new BusinessCommercialPlanDto(
            configuration.ProductType.ToString(),
            configuration.PlanType.ToString(),
            configuration.DisplayName,
            configuration.Description,
            configuration.MonthlyPrice,
            configuration.AnnualPrice,
            configuration.Enabled,
            Services.BusinessCommercialPlanCatalog.IsAvailableForPurchase(configuration.PlanType),
            configuration.DisplayOrder,
            definition.Capabilities.Select(capability => capability.ToString()).ToArray());
    }
}