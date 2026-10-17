using Clienta.Api.Models;

namespace Clienta.Api.Services;

public static class BusinessPlanEntitlement
{
    public static bool HasCurrentBusinessCapabilities(TenantProductType? productType, PlanType plan) =>
        productType == TenantProductType.Business && plan is PlanType.Trial or PlanType.Basic;
}
