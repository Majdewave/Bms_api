using Clienta.Api.Models;

namespace Clienta.Api.Services;

public static class BusinessPlanEntitlement
{
    public static bool HasCurrentBusinessCapabilities(TenantProductType? productType, PlanType plan) =>
        productType == TenantProductType.Business && plan is PlanType.Trial or PlanType.Basic;

    public static bool HasActiveMedicalImagingEntitlement(
        PlanType plan,
        SubscriptionStatus subscriptionStatus,
        DateTime? trialEndsAt,
        DateTime? subscriptionEndsAt,
        bool isSuspended,
        DateTime utcNow)
    {
        if (isSuspended)
            return false;

        return plan switch
        {
            PlanType.Trial =>
                subscriptionStatus == SubscriptionStatus.Trialing &&
                trialEndsAt.HasValue &&
                trialEndsAt.Value > utcNow,
            PlanType.Enterprise =>
                subscriptionStatus == SubscriptionStatus.Active &&
                (!subscriptionEndsAt.HasValue || subscriptionEndsAt.Value > utcNow),
            _ => false
        };
    }
}
