using Stripe;

namespace Clienta.Api.Services;

public interface IStripeSubscriptionGateway
{
    Task<Subscription> GetSubscriptionAsync(string subscriptionId);
    Task<Subscription> UpdateSubscriptionAsync(string subscriptionId, SubscriptionUpdateOptions options);
    Task<SubscriptionSchedule> CreateScheduleFromSubscriptionAsync(string subscriptionId);
    Task<SubscriptionSchedule> GetScheduleAsync(string scheduleId);
    Task<SubscriptionSchedule> UpdateScheduleAsync(string scheduleId, SubscriptionScheduleUpdateOptions options);
}

public sealed class StripeSubscriptionGateway : IStripeSubscriptionGateway
{
    public Task<Subscription> GetSubscriptionAsync(string subscriptionId) =>
        new SubscriptionService().GetAsync(subscriptionId);

    public Task<Subscription> UpdateSubscriptionAsync(string subscriptionId, SubscriptionUpdateOptions options) =>
        new SubscriptionService().UpdateAsync(subscriptionId, options);

    public Task<SubscriptionSchedule> CreateScheduleFromSubscriptionAsync(string subscriptionId) =>
        new SubscriptionScheduleService().CreateAsync(new SubscriptionScheduleCreateOptions
        {
            FromSubscription = subscriptionId
        });

    public Task<SubscriptionSchedule> GetScheduleAsync(string scheduleId) =>
        new SubscriptionScheduleService().GetAsync(scheduleId);

    public Task<SubscriptionSchedule> UpdateScheduleAsync(string scheduleId, SubscriptionScheduleUpdateOptions options) =>
        new SubscriptionScheduleService().UpdateAsync(scheduleId, options);
}
