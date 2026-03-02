using Clienta.Api.Models;

namespace Clienta.Api.Entities;

public class Tenant
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;
    public string Subdomain { get; set; } = default!;
    public string? LogoUrl { get; set; }

    public PlanType Plan { get; set; } = PlanType.Trial;
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trialing;

    public DateTime? TrialEndsAt { get; set; }
    public bool IsSuspended { get; set; }

    public int UserLimit { get; set; } = 10;
    public int MessageLimit { get; set; } = 100;

    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    // Grace Period - don't suspend immediately on payment failure
    public DateTime? PaymentGracePeriodEndsAt { get; set; }

    // Scheduled Plan Change - applies at end of billing period (for downgrades)
    public PlanType? ScheduledPlan { get; set; }
    public DateTime? ScheduledPlanChangeAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
}
