namespace Clienta.Api.Models;

/// <summary>
/// Subscription status tracking for billing lifecycle
/// </summary>
public enum SubscriptionStatus
{
    Trialing = 0,
    Active = 1,
    PastDue = 2,
    GracePeriod = 3,
    Canceled = 4,
    Unpaid = 5,
    PendingApproval = 6
}
