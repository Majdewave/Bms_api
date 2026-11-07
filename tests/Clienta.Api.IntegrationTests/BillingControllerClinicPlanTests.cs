using System.Reflection;
using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class BillingControllerClinicPlanTests
{
    [TestMethod]
    public async Task ClinicProToBasic_IsAcceptedAsScheduledChange()
    {
        await using var fixture = await Fixture.CreateAsync(TenantProductType.Clinic, PlanType.Pro);

        var result = await fixture.Controller.ChangePlan(new ChangePlanRequest(PlanType.Basic, BillingCycle.Monthly));

        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.AreEqual(PlanType.Basic, fixture.StripeService.LastPlan);
        Assert.AreEqual(BillingCycle.Monthly, fixture.StripeService.LastCycle);
        Assert.AreEqual("downgrade", ReadProperty<string>(((OkObjectResult)result).Value!, "type"));
    }

    [TestMethod]
    public async Task ClinicBasicToEnterprise_IsAcceptedAsImmediateUpgrade()
    {
        await using var fixture = await Fixture.CreateAsync(TenantProductType.Clinic, PlanType.Basic);

        var result = await fixture.Controller.ChangePlan(new ChangePlanRequest(PlanType.Enterprise, BillingCycle.Monthly));

        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.AreEqual(PlanType.Enterprise, fixture.StripeService.LastPlan);
        Assert.IsTrue(ReadProperty<bool>(((OkObjectResult)result).Value!, "effectiveImmediately"));
    }

    [TestMethod]
    public async Task ClinicProSelection_IsRejectedWithoutCallingStripeService()
    {
        await using var fixture = await Fixture.CreateAsync(TenantProductType.Clinic, PlanType.Basic);

        var result = await fixture.Controller.ChangePlan(new ChangePlanRequest(PlanType.Pro, BillingCycle.Monthly));

        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        StringAssert.Contains(ReadProperty<string>(((BadRequestObjectResult)result).Value!, "error"), "Coming Soon");
        Assert.AreEqual(0, fixture.StripeService.ChangePlanCalls);
    }

    [TestMethod]
    public async Task ClinicYearlySelection_IsRejectedWithoutCallingStripeService()
    {
        await using var fixture = await Fixture.CreateAsync(TenantProductType.Clinic, PlanType.Basic);

        var result = await fixture.Controller.ChangePlan(new ChangePlanRequest(PlanType.Enterprise, BillingCycle.Yearly));

        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        StringAssert.Contains(ReadProperty<string>(((BadRequestObjectResult)result).Value!, "error"), "Monthly");
        Assert.AreEqual(0, fixture.StripeService.ChangePlanCalls);
    }

    [TestMethod]
    public async Task NonClinicProSelection_RetainsExistingChangePlanBehavior()
    {
        await using var fixture = await Fixture.CreateAsync(null, PlanType.Basic);

        var result = await fixture.Controller.ChangePlan(new ChangePlanRequest(PlanType.Pro, BillingCycle.Monthly));

        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.AreEqual(1, fixture.StripeService.ChangePlanCalls);
        Assert.AreEqual(PlanType.Pro, fixture.StripeService.LastPlan);
    }

    [TestMethod]
    public async Task BillingStatus_IncludesScheduledPlanAndEffectiveDate()
    {
        await using var fixture = await Fixture.CreateAsync(TenantProductType.Clinic, PlanType.Pro);
        var effectiveAt = new DateTime(2026, 12, 17, 8, 30, 0, DateTimeKind.Utc);
        fixture.Tenant.ScheduledPlan = PlanType.Basic;
        fixture.Tenant.ScheduledPlanChangeAt = effectiveAt;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.GetBillingStatus();

        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        var value = ((OkObjectResult)result).Value!;
        Assert.AreEqual("Basic", ReadProperty<string>(value, "scheduledPlan"));
        Assert.AreEqual(effectiveAt, ReadProperty<DateTime?>(value, "scheduledPlanChangeAt"));
    }

    private static T ReadProperty<T>(object source, string name)
    {
        var value = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
        Assert.IsNotNull(value, $"Expected response property '{name}'.");
        return (T)value;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, AppDbContext db, Tenant tenant, TestTenantContext tenantContext)
        {
            _connection = connection;
            Db = db;
            Tenant = tenant;
            StripeService = new FakeStripeService();
            Controller = new BillingController(
                StripeService,
                tenantContext,
                db,
                new ConfigurationBuilder().Build(),
                NullLogger<BillingController>.Instance);
        }

        public AppDbContext Db { get; }
        public Tenant Tenant { get; }
        public FakeStripeService StripeService { get; }
        public BillingController Controller { get; }

        public static async Task<Fixture> CreateAsync(TenantProductType? productType, PlanType plan)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
            var tenantContext = new TestTenantContext();
            tenantContext.SetTenant(Guid.NewGuid());
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, tenantContext);
            await db.Database.EnsureCreatedAsync();

            var tenant = new Tenant
            {
                Id = tenantContext.TenantId,
                Name = "Billing controller test tenant",
                Subdomain = $"billing-test-{Guid.NewGuid():N}",
                ProductType = productType,
                Plan = plan,
                BillingCycle = BillingCycle.Monthly,
                SubscriptionStatus = SubscriptionStatus.Active,
                StripeCustomerId = "cus_test",
                StripeSubscriptionId = "sub_test"
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            return new Fixture(connection, db, tenant, tenantContext);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private sealed class FakeStripeService : IStripeService
    {
        public int ChangePlanCalls { get; private set; }
        public PlanType? LastPlan { get; private set; }
        public BillingCycle? LastCycle { get; private set; }

        public Task<string> CreateCheckoutSessionAsync(Guid tenantId, PlanType planType, BillingCycle billingCycle) =>
            Task.FromResult("https://example.test/checkout");

        public Task ChangePlanAsync(Guid tenantId, PlanType newPlan, BillingCycle newCycle)
        {
            ChangePlanCalls++;
            LastPlan = newPlan;
            LastCycle = newCycle;
            return Task.CompletedTask;
        }

        public Task HandleWebhookAsync(string json, string stripeSignature) => Task.CompletedTask;
    }
}
