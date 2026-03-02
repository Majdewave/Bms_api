namespace Clienta.Api.Entities;

/// <summary>
/// Tracks processed Stripe webhook events to ensure idempotency
/// Prevents duplicate processing if Stripe sends the same event multiple times
/// </summary>
public class ProcessedStripeEvent
{
    public string EventId { get; set; } = default!;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
