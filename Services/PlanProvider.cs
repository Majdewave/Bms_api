using Clienta.Api.Models;

namespace Clienta.Api.Services;

public interface IPlanProvider
{
    PlanDefinition GetPlan(PlanType plan);
}

public class PlanProvider : IPlanProvider
{
    public PlanDefinition GetPlan(PlanType plan)
    {
        return plan switch
        {
            PlanType.Trial => new PlanDefinition
            {
                Plan = plan,
                UserLimit = 10,
                MessageLimit = 1000,
                AllowCustomBranding = false,
                EmailAutomation = true,
                Priority = "Standard",
                Support = "Email only"
            },

            PlanType.Basic => new PlanDefinition
            {
                Plan = plan,
                UserLimit = 25,
                MessageLimit = 5000,
                AllowCustomBranding = true,
                EmailAutomation = true,
                Priority = "Standard",
                Support = "Email support"
            },

            PlanType.Pro => new PlanDefinition
            {
                Plan = plan,
                UserLimit = -1, // Unlimited
                MessageLimit = -1, // Unlimited
                AllowCustomBranding = true,
                EmailAutomation = true,
                Priority = "High",
                Support = "24/7 Priority"
            },

            PlanType.Enterprise => new PlanDefinition
            {
                Plan = plan,
                UserLimit = -1, // Unlimited
                MessageLimit = -1, // Unlimited
                AllowCustomBranding = true,
                EmailAutomation = true,
                Priority = "Critical",
                Support = "Dedicated Account Manager"
            },

            _ => throw new InvalidOperationException($"Unknown plan: {plan}")
        };
    }
}
