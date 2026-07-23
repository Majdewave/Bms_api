using Clienta.Api.Entities;
using Clienta.Api.Models;

namespace Clienta.Api.Services.Platform;

internal static class PlatformTenantPresentationMapper
{
    public static string ToBusinessPlan(PlanType plan)
    {
        return plan switch
        {
            PlanType.Basic => "Starter",
            PlanType.Pro => "Growth",
            PlanType.Enterprise => "Enterprise",
            PlanType.Trial => "Starter",
            _ => "Starter"
        };
    }

    public static string ComputeBusinessStatus(Tenant tenant)
    {
        return ComputeBusinessStatus(
            tenant.SubscriptionStatus,
            tenant.IsTrial,
            tenant.TrialEndsAt,
            tenant.IsSuspended,
            DateTime.UtcNow);
    }

    public static string ComputeBusinessStatus(
        SubscriptionStatus subscriptionStatus,
        bool isTrial,
        DateTime? trialEndsAt,
        bool isSuspended,
        DateTime nowUtc)
    {
        if (isSuspended)
        {
            return "Suspended";
        }

        if (subscriptionStatus == SubscriptionStatus.PendingApproval)
        {
            return "Pending";
        }

        if (trialEndsAt.HasValue)
        {
            if (trialEndsAt.Value <= nowUtc)
            {
                return "Expired";
            }

            return "Trial";
        }

        if (subscriptionStatus == SubscriptionStatus.Active)
        {
            return "Active";
        }

        if (subscriptionStatus is SubscriptionStatus.Canceled or SubscriptionStatus.Unpaid or SubscriptionStatus.PastDue or SubscriptionStatus.GracePeriod)
        {
            return "Expired";
        }

        return "Pending";
    }
}
