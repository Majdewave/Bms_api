using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class StripeScheduledPlanChangeTests
{
    private const string WebhookSecret = "whsec_test_scheduled_plan_change";
    private const string ClinicBasicPrice = "price_1UX7WT3A6zs3LHbOnI6cDKNQ";
    private const string ClinicEnterprisePrice = "price_clinic_enterprise_test";
    private const string ClinicProPrice = "price_clinic_pro_test";

    [TestMethod]
    public async Task ClinicBasicToEnterprise_UpdatesExistingSubscriptionImmediatelyWithProration()
    {
        await using var fixture = await Fixture.CreateAsync(
            TenantProductType.Clinic,
            PlanType.Basic,
            "price_clinic_basic_current",
            ClinicBasicPrice,
            ClinicEnterprisePrice);

        fixture.Tenant.ScheduledPlan = PlanType.Basic;
        fixture.Tenant.ScheduledPlanChangeAt = DateTime.UtcNow.AddDays(10);
        fixture.Gateway.Subscription = CreateSubscription("sub_clinic", "price_clinic_basic_current", DateTime.UtcNow.AddDays(20));

        await fixture.Service.ChangePlanAsync(fixture.Tenant.Id, PlanType.Enterprise, BillingCycle.Monthly);

        Assert.AreEqual("sub_clinic", fixture.Gateway.UpdatedSubscriptionId);
        Assert.AreEqual("create_prorations", fixture.Gateway.SubscriptionUpdateOptions?.ProrationBehavior);
        Assert.AreEqual(ClinicEnterprisePrice, fixture.Gateway.SubscriptionUpdateOptions?.Items.Single().Price);
        Assert.AreEqual(PlanType.Enterprise, fixture.Tenant.Plan);
        Assert.AreEqual(new PlanProvider().GetPlan(PlanType.Enterprise).UserLimit, fixture.Tenant.UserLimit);
        Assert.AreEqual(new PlanProvider().GetPlan(PlanType.Enterprise).MessageLimit, fixture.Tenant.MessageLimit);
        Assert.IsNull(fixture.Tenant.ScheduledPlan);
        Assert.IsNull(fixture.Tenant.ScheduledPlanChangeAt);
        Assert.AreEqual(0, fixture.Gateway.CreateScheduleCount);
    }

    [DataTestMethod]
    [DataRow(PlanType.Enterprise, ClinicEnterprisePrice)]
    [DataRow(PlanType.Pro, ClinicProPrice)]
    public async Task ClinicToBasic_UsesExistingSubscriptionAndExactStripeBillingBoundary(
        PlanType currentPlan,
        string currentPriceId)
    {
        var boundary = new DateTime(2026, 12, 17, 8, 30, 0, DateTimeKind.Utc);
        await using var fixture = await Fixture.CreateAsync(
            TenantProductType.Clinic,
            currentPlan,
            currentPriceId,
            ClinicBasicPrice,
            ClinicEnterprisePrice,
            ClinicProPrice);
        fixture.Gateway.Subscription = CreateSubscription("sub_existing", currentPriceId, boundary);
        fixture.Gateway.Subscription.ScheduleId = "sched_existing";
        fixture.Gateway.Schedule = CreateSchedule("sched_existing", currentPriceId, boundary);
        var existingUserLimit = fixture.Tenant.UserLimit;
        var existingMessageLimit = fixture.Tenant.MessageLimit;

        await fixture.Service.ChangePlanAsync(fixture.Tenant.Id, PlanType.Basic, BillingCycle.Monthly);

        Assert.AreEqual("sched_existing", fixture.Gateway.GetScheduleId);
        Assert.IsNull(fixture.Gateway.CreatedScheduleFromSubscriptionId);
        Assert.AreEqual(0, fixture.Gateway.CreateScheduleCount);
        Assert.AreEqual("sched_existing", fixture.Gateway.UpdatedScheduleId);
        Assert.AreEqual(0, fixture.Gateway.SubscriptionUpdateCount);
        Assert.AreEqual(PlanType.Basic, fixture.Tenant.ScheduledPlan);
        Assert.AreEqual(boundary, fixture.Tenant.ScheduledPlanChangeAt);
        Assert.AreEqual(currentPlan, fixture.Tenant.Plan, "Clienta must retain the active plan until Stripe applies the target price.");
        Assert.AreEqual(existingUserLimit, fixture.Tenant.UserLimit);
        Assert.AreEqual(existingMessageLimit, fixture.Tenant.MessageLimit);
        Assert.AreEqual("release", fixture.Gateway.ScheduleUpdateOptions?.EndBehavior);
        Assert.AreEqual("none", fixture.Gateway.ScheduleUpdateOptions?.ProrationBehavior);
        Assert.AreEqual(2, fixture.Gateway.ScheduleUpdateOptions?.Phases.Count);
        var phases = fixture.Gateway.ScheduleUpdateOptions!.Phases;
        Assert.AreEqual(boundary, (DateTime?)phases[0].EndDate);
        Assert.AreEqual(boundary, (DateTime?)phases[1].StartDate);
        Assert.IsNull(phases[0].AddInvoiceItems);
        Assert.IsNull(phases[0].DefaultTaxRates);
        Assert.IsNull(phases[0].Discounts);
        Assert.IsNull(phases[0].Metadata);
        Assert.IsNull(phases[0].Items.Single().Discounts);
        Assert.IsNull(phases[0].Items.Single().TaxRates);
        Assert.IsNull(phases[0].Items.Single().Metadata);
        Assert.AreEqual("none", fixture.Gateway.ScheduleUpdateOptions?.Phases[1].ProrationBehavior);
        Assert.AreEqual(ClinicBasicPrice, fixture.Gateway.ScheduleUpdateOptions?.Phases[1].Items.Single().Price);
        Assert.IsNull(phases[1].AddInvoiceItems);
        Assert.IsNull(phases[1].DefaultTaxRates);
        Assert.IsNull(phases[1].Discounts);
        Assert.IsNull(phases[1].Metadata);
        Assert.IsNull(phases[1].Items.Single().Discounts);
        Assert.IsNull(phases[1].Items.Single().TaxRates);
        Assert.IsNull(phases[1].Items.Single().Metadata);
        Assert.AreEqual(1, fixture.Gateway.ScheduleUpdateOptions?.Phases[1].Duration?.IntervalCount);
        Assert.AreEqual("month", fixture.Gateway.ScheduleUpdateOptions?.Phases[1].Duration?.Interval);
    }

    [TestMethod]
    public async Task BusinessUpgrade_RetainsExistingImmediateProrationBehavior()
    {
        await using var fixture = await Fixture.CreateAsync(
            TenantProductType.Business,
            PlanType.Basic,
            "price_business_basic",
            "price_business_pro");
        fixture.Gateway.Subscription = CreateSubscription("sub_business", "price_business_basic", DateTime.UtcNow.AddDays(20));

        await fixture.Service.ChangePlanAsync(fixture.Tenant.Id, PlanType.Pro, BillingCycle.Monthly);

        Assert.AreEqual("sub_business", fixture.Gateway.UpdatedSubscriptionId);
        Assert.AreEqual("create_prorations", fixture.Gateway.SubscriptionUpdateOptions?.ProrationBehavior);
        Assert.AreEqual(PlanType.Pro, fixture.Tenant.Plan);
        Assert.AreEqual(0, fixture.Gateway.CreateScheduleCount);
    }

    [TestMethod]
    public async Task ClinicProPriceMissing_FailsBeforeAnyStripeSubscriptionOperation()
    {
        await using var fixture = await Fixture.CreateAsync(
            TenantProductType.Clinic,
            PlanType.Basic,
            "price_clinic_basic_current",
            ClinicBasicPrice);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            fixture.Service.ChangePlanAsync(fixture.Tenant.Id, PlanType.Pro, BillingCycle.Monthly));

        Assert.AreEqual(0, fixture.Gateway.GetSubscriptionCount);
        Assert.IsNull(fixture.Tenant.ScheduledPlan);
        Assert.AreEqual(PlanType.Basic, fixture.Tenant.Plan);
    }

    [TestMethod]
    public async Task SubscriptionUpdatedBeforeStripeTransition_DoesNotActivateScheduledPlanEvenAfterDate()
    {
        await using var fixture = await Fixture.CreateAsync(
            TenantProductType.Clinic,
            PlanType.Enterprise,
            ClinicEnterprisePrice,
            ClinicBasicPrice);
        fixture.Tenant.ScheduledPlan = PlanType.Basic;
        fixture.Tenant.ScheduledPlanChangeAt = DateTime.UtcNow.AddDays(-1);
        fixture.Tenant.StripeSubscriptionId = "sub_webhook";
        fixture.Gateway.Subscription = CreateSubscription("sub_webhook", ClinicEnterprisePrice, DateTime.UtcNow.AddDays(10));
        await fixture.Db.SaveChangesAsync();

        await fixture.SendWebhookAsync("customer.subscription.updated", "evt_before_transition", SubscriptionWebhookObject("sub_webhook", ClinicEnterprisePrice));

        Assert.AreEqual(PlanType.Enterprise, fixture.Tenant.Plan);
        Assert.AreEqual(PlanType.Basic, fixture.Tenant.ScheduledPlan);
        Assert.IsNotNull(fixture.Tenant.ScheduledPlanChangeAt);
        Assert.AreEqual(new PlanProvider().GetPlan(PlanType.Enterprise).UserLimit, fixture.Tenant.UserLimit);
    }

    [TestMethod]
    public async Task SubscriptionUpdatedAfterStripeTransition_ActivatesPlanAndClearsScheduleIdempotently()
    {
        await using var fixture = await Fixture.CreateAsync(
            TenantProductType.Clinic,
            PlanType.Enterprise,
            ClinicEnterprisePrice,
            ClinicBasicPrice);
        fixture.Tenant.ScheduledPlan = PlanType.Basic;
        fixture.Tenant.ScheduledPlanChangeAt = DateTime.UtcNow.AddDays(-1);
        fixture.Tenant.StripeSubscriptionId = "sub_webhook";
        fixture.Gateway.Subscription = CreateSubscription("sub_webhook", ClinicBasicPrice, DateTime.UtcNow.AddDays(29));
        await fixture.Db.SaveChangesAsync();
        var webhookObject = SubscriptionWebhookObject("sub_webhook", ClinicBasicPrice);

        await fixture.SendWebhookAsync("customer.subscription.updated", "evt_after_transition", webhookObject);
        await fixture.SendWebhookAsync("customer.subscription.updated", "evt_after_transition", webhookObject);

        Assert.AreEqual(PlanType.Basic, fixture.Tenant.Plan);
        Assert.AreEqual(new PlanProvider().GetPlan(PlanType.Basic).UserLimit, fixture.Tenant.UserLimit);
        Assert.AreEqual(new PlanProvider().GetPlan(PlanType.Basic).MessageLimit, fixture.Tenant.MessageLimit);
        Assert.IsNull(fixture.Tenant.ScheduledPlan);
        Assert.IsNull(fixture.Tenant.ScheduledPlanChangeAt);
        Assert.AreEqual(1, await fixture.Db.ProcessedStripeEvents.CountAsync());
    }

    [TestMethod]
    public async Task ScheduleUpdatedBeforeTransition_DoesNotActivatePlan()
    {
        await using var fixture = await Fixture.CreateAsync(
            TenantProductType.Clinic,
            PlanType.Enterprise,
            ClinicEnterprisePrice,
            ClinicBasicPrice);
        fixture.Tenant.ScheduledPlan = PlanType.Basic;
        fixture.Tenant.ScheduledPlanChangeAt = DateTime.UtcNow.AddDays(-1);
        fixture.Tenant.StripeSubscriptionId = "sub_webhook";
        fixture.Gateway.Subscription = CreateSubscription("sub_webhook", ClinicEnterprisePrice, DateTime.UtcNow.AddDays(10));
        await fixture.Db.SaveChangesAsync();

        await fixture.SendWebhookAsync(
            "subscription_schedule.updated",
            "evt_schedule_before_transition",
            new { id = "sched_webhook", @object = "subscription_schedule", subscription = "sub_webhook", status = "active" });

        Assert.AreEqual(PlanType.Enterprise, fixture.Tenant.Plan);
        Assert.AreEqual(PlanType.Basic, fixture.Tenant.ScheduledPlan);
    }

    private static Subscription CreateSubscription(string id, string priceId, DateTime currentPeriodEnd) => new()
    {
        Id = id,
        Status = "active",
        Items = new StripeList<SubscriptionItem>
        {
            Data = new List<SubscriptionItem>
            {
                new()
                {
                    Id = "si_existing",
                    CurrentPeriodStart = currentPeriodEnd.AddMonths(-1),
                    CurrentPeriodEnd = currentPeriodEnd,
                    Price = new Price { Id = priceId },
                    Quantity = 1
                }
            }
        }
    };

    private static SubscriptionSchedule CreateSchedule(string id, string currentPriceId, DateTime currentPeriodEnd) => new()
    {
        Id = id,
        Status = "active",
        Phases = new List<SubscriptionSchedulePhase>
        {
            new()
            {
                StartDate = currentPeriodEnd.AddMonths(-1),
                EndDate = currentPeriodEnd,
                Items = new List<SubscriptionSchedulePhaseItem>
                {
                    new() { PriceId = currentPriceId, Quantity = 1 }
                },
                Metadata = new Dictionary<string, string>()
            }
        }
    };

    private static object SubscriptionWebhookObject(string subscriptionId, string activePriceId)
    {
        var currentPeriodEnd = DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds();
        return new
        {
            id = subscriptionId,
            @object = "subscription",
            customer = "cus_test",
            status = "active",
            items = new
            {
                @object = "list",
                data = new[]
                {
                    new
                    {
                        id = "si_webhook",
                        @object = "subscription_item",
                        current_period_start = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeSeconds(),
                        current_period_end = currentPeriodEnd,
                        price = new { id = activePriceId, @object = "price" },
                        quantity = 1
                    }
                },
                has_more = false,
                url = "/v1/subscription_items"
            }
        };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, AppDbContext db, Tenant tenant, FakeStripeSubscriptionGateway gateway, IConfiguration configuration)
        {
            _connection = connection;
            Db = db;
            Tenant = tenant;
            Gateway = gateway;
            Service = new StripeService(db, configuration, new NoOpEmailService(), gateway);
        }

        public AppDbContext Db { get; }
        public Tenant Tenant { get; }
        public FakeStripeSubscriptionGateway Gateway { get; }
        public StripeService Service { get; }

        public static async Task<Fixture> CreateAsync(
            TenantProductType? productType,
            PlanType currentPlan,
            string currentPriceId,
            string? clinicBasicPrice,
            string? clinicEnterprisePrice = null,
            string? clinicProPrice = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
            var tenantContext = new TestTenantContext();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, tenantContext);
            await db.Database.EnsureCreatedAsync();

            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = "Stripe test tenant",
                Subdomain = $"stripe-test-{Guid.NewGuid():N}",
                ProductType = productType,
                Plan = currentPlan,
                UserLimit = new PlanProvider().GetPlan(currentPlan).UserLimit,
                MessageLimit = new PlanProvider().GetPlan(currentPlan).MessageLimit,
                StripeCustomerId = "cus_existing",
                StripeSubscriptionId = "sub_existing"
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            var configurationValues = new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = "sk_test_only_not_used",
                ["Stripe:WebhookSecret"] = WebhookSecret,
                ["Stripe:Clinic:BasicMonthlyPriceId"] = clinicBasicPrice,
                ["Stripe:Clinic:EnterpriseMonthlyPriceId"] = clinicEnterprisePrice,
                ["Stripe:Clinic:ProMonthlyPriceId"] = clinicProPrice,
                ["Stripe:BasicMonthlyPriceId"] = "price_business_basic_test",
                ["Stripe:ProMonthlyPriceId"] = "price_business_pro_test",
                ["Stripe:ProYearlyPriceId"] = "price_business_pro_yearly_test"
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(configurationValues).Build();
            var gateway = new FakeStripeSubscriptionGateway
            {
                Subscription = CreateSubscription("sub_existing", currentPriceId, DateTime.UtcNow.AddDays(20))
            };

            return new Fixture(connection, db, tenant, gateway, configuration);
        }

        public async Task SendWebhookAsync(string eventType, string eventId, object eventObject)
        {
            var payload = JsonSerializer.Serialize(new
            {
                id = eventId,
                @object = "event",
                api_version = "2024-06-20",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                data = new { @object = eventObject },
                livemode = false,
                pending_webhooks = 1,
                request = new { id = (string?)null, idempotency_key = (string?)null },
                type = eventType
            });
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signedPayload = $"{timestamp}.{payload}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
            var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();
            var stripeSignature = $"t={timestamp},v1={signature}";
            if (eventType == "customer.subscription.updated")
            {
                var parsed = EventUtility.ConstructEvent(payload, stripeSignature, WebhookSecret, throwOnApiVersionMismatch: false);
                Assert.IsInstanceOfType(parsed.Data.Object, typeof(Subscription), $"Unexpected event object type: {parsed.Data.Object?.GetType().FullName ?? "null"}");
            }
            await Service.HandleWebhookAsync(payload, stripeSignature);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeStripeSubscriptionGateway : IStripeSubscriptionGateway
    {
        public Subscription Subscription { get; set; } = new();
        public SubscriptionSchedule Schedule { get; set; } = new();
        public string? CreatedScheduleFromSubscriptionId { get; private set; }
        public string? GetScheduleId { get; private set; }
        public string? UpdatedScheduleId { get; private set; }
        public string? UpdatedSubscriptionId { get; private set; }
        public SubscriptionUpdateOptions? SubscriptionUpdateOptions { get; private set; }
        public SubscriptionScheduleUpdateOptions? ScheduleUpdateOptions { get; private set; }
        public int CreateScheduleCount { get; private set; }
        public int GetSubscriptionCount { get; private set; }
        public int SubscriptionUpdateCount { get; private set; }

        public Task<Subscription> GetSubscriptionAsync(string subscriptionId)
        {
            GetSubscriptionCount++;
            return Task.FromResult(Subscription);
        }

        public Task<Subscription> UpdateSubscriptionAsync(string subscriptionId, SubscriptionUpdateOptions options)
        {
            UpdatedSubscriptionId = subscriptionId;
            SubscriptionUpdateOptions = options;
            SubscriptionUpdateCount++;
            return Task.FromResult(Subscription);
        }

        public Task<SubscriptionSchedule> CreateScheduleFromSubscriptionAsync(string subscriptionId)
        {
            CreatedScheduleFromSubscriptionId = subscriptionId;
            CreateScheduleCount++;
            return Task.FromResult(Schedule);
        }

        public Task<SubscriptionSchedule> GetScheduleAsync(string scheduleId)
        {
            GetScheduleId = scheduleId;
            return Task.FromResult(Schedule);
        }

        public Task<SubscriptionSchedule> UpdateScheduleAsync(string scheduleId, SubscriptionScheduleUpdateOptions options)
        {
            UpdatedScheduleId = scheduleId;
            ScheduleUpdateOptions = options;
            return Task.FromResult(Schedule);
        }
    }

    private sealed class NoOpEmailService : IEmailService
    {
        public Task SendInviteEmailAsync(string email, string inviteLink) => Task.CompletedTask;
        public Task SendPasswordResetEmailAsync(string email, string resetLink) => Task.CompletedTask;
        public Task SendEmailAsync(string email, string subject, string body) => Task.CompletedTask;
        public Task SendTrialReminderAsync(string email, string companyName, string subdomain, int daysLeft, Guid tenantId) => Task.CompletedTask;
        public Task SendTrialExpiredAsync(string email, string companyName, string subdomain, Guid tenantId) => Task.CompletedTask;
    }
}
