using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Filters;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class BusinessCapabilityEntitlementTests
{
    [DataTestMethod]
    [DataRow(TenantProductType.Business, PlanType.Trial, true)]
    [DataRow(TenantProductType.Business, PlanType.Basic, true)]
    [DataRow(TenantProductType.Business, PlanType.Pro, false)]
    [DataRow(TenantProductType.Clinic, PlanType.Trial, false)]
    [DataRow(TenantProductType.Clinic, PlanType.Basic, false)]
    public async Task FeatureService_EntitlesOnlyBusinessTrialAndBasic(TenantProductType product, PlanType plan, bool expected)
    {
        var (db, connection, tenantContext, tenantId) = await CreateTenantAsync(product, plan, quotesEnabled: false, invoicesEnabled: false);
        await using (db)
        await using (connection)
        {
            var features = new FeatureService(db, tenantContext, new MedicalImagingEntitlementService(db));
            Assert.AreEqual(expected, await features.IsEnabledAsync("quotes"));
            Assert.AreEqual(expected, await features.IsEnabledAsync("invoices"));
        }
    }

    [TestMethod]
    public async Task DepartmentQuoteFeature_BusinessCurrentPlansBypassLegacyTenantAndDepartmentFlags()
    {
        var (db, connection, _, tenantId) = await CreateTenantAsync(TenantProductType.Business, PlanType.Trial, quotesEnabled: false, invoicesEnabled: false);
        await using (db)
        await using (connection)
        {
            var departmentId = Guid.NewGuid();
            db.Departments.Add(new Department
            {
                Id = departmentId,
                TenantId = tenantId,
                Name = "Business",
            });
            db.DepartmentFeatures.Add(new DepartmentFeature
            {
                Id = Guid.NewGuid(),
                DepartmentId = departmentId,
                FeatureKey = "quotesEnabled",
                IsEnabled = false,
            });
            await db.SaveChangesAsync();

            Assert.IsTrue(await new DepartmentFeatureResolver(db, new MedicalImagingEntitlementService(db))
                .IsFeatureEnabledAsync(tenantId, departmentId, "quotesEnabled"));
        }
    }

    [TestMethod]
    public async Task DepartmentQuoteFeature_PreservesClinicAndLegacyFlags()
    {
        foreach (var product in new TenantProductType?[] { TenantProductType.Clinic, null })
        {
            var (db, connection, _, tenantId) = await CreateTenantAsync(product, PlanType.Pro, quotesEnabled: false, invoicesEnabled: false);
            await using (db)
            await using (connection)
            {
                Assert.IsFalse(await new DepartmentFeatureResolver(db, new MedicalImagingEntitlementService(db))
                    .IsFeatureEnabledAsync(tenantId, null, "quotesEnabled"));
            }
        }
    }

    [TestMethod]
    public void MedicalImagingEntitlement_RequiresAnActiveTrialOrEnterprisePlan()
    {
        var now = DateTime.UtcNow;

        Assert.IsTrue(BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            PlanType.Trial, SubscriptionStatus.Trialing, now.AddDays(1), null, false, now));
        Assert.IsFalse(BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            PlanType.Trial, SubscriptionStatus.Trialing, now, null, false, now));
        Assert.IsFalse(BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            PlanType.Trial, SubscriptionStatus.Active, now.AddDays(1), null, false, now));
        Assert.IsTrue(BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            PlanType.Enterprise, SubscriptionStatus.Active, null, null, false, now));
        Assert.IsFalse(BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            PlanType.Enterprise, SubscriptionStatus.PastDue, null, null, false, now));
        Assert.IsFalse(BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            PlanType.Enterprise, SubscriptionStatus.Active, null, now, false, now));
        Assert.IsFalse(BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            PlanType.Enterprise, SubscriptionStatus.Active, null, null, true, now));
        Assert.IsFalse(BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            PlanType.Basic, SubscriptionStatus.Active, null, null, false, now));
        Assert.IsFalse(BusinessPlanEntitlement.HasActiveMedicalImagingEntitlement(
            PlanType.Pro, SubscriptionStatus.Active, null, null, false, now));
    }

    [TestMethod]
    public async Task MedicalImagingFeature_BasicAndProCannotUseStaleEnabledFlags()
    {
        foreach (var plan in new[] { PlanType.Basic, PlanType.Pro })
        {
            var (db, connection, tenantContext, tenantId) = await CreateTenantAsync(
                TenantProductType.Business,
                plan,
                quotesEnabled: false,
                invoicesEnabled: false,
                subscriptionStatus: SubscriptionStatus.Active,
                medicalImagingEnabled: true);
            await using (db)
            await using (connection)
            {
                var entitlementService = new MedicalImagingEntitlementService(db);
                var featureService = new FeatureService(db, tenantContext, entitlementService);
                var departmentResolver = new DepartmentFeatureResolver(db, entitlementService);
                var userFeatureAccess = new UserDepartmentFeatureAccessService(
                    db,
                    tenantContext,
                    departmentResolver,
                    entitlementService);

                Assert.IsFalse((await featureService.GetEffectiveAsync()).MedicalImagingEnabled);
                Assert.IsFalse(await featureService.IsEnabledAsync("medicalImaging"));
                Assert.IsTrue(await departmentResolver.IsFeatureEnabledAsync(
                    tenantId,
                    null,
                    "medicalImagingEnabled"));
                Assert.IsFalse(await userFeatureAccess.CanUserAccessFeatureAsync(
                    tenantId,
                    Guid.NewGuid(),
                    "medicalImagingEnabled"));
                Assert.IsTrue((await db.TenantFeatures.SingleAsync()).MedicalImagingEnabled);
            }
        }
    }

    [TestMethod]
    public async Task MedicalImagingFeature_ActiveTrialAndEnterpriseHonorTheExistingToggle()
    {
        var entitledPlans = new[]
        {
            (Plan: PlanType.Trial, Status: SubscriptionStatus.Trialing, TrialEndsAt: (DateTime?)DateTime.UtcNow.AddDays(1)),
            (Plan: PlanType.Enterprise, Status: SubscriptionStatus.Active, TrialEndsAt: (DateTime?)null),
        };

        foreach (var plan in entitledPlans)
        {
            var (db, connection, tenantContext, tenantId) = await CreateTenantAsync(
                null,
                plan.Plan,
                quotesEnabled: false,
                invoicesEnabled: false,
                subscriptionStatus: plan.Status,
                trialEndsAt: plan.TrialEndsAt,
                medicalImagingEnabled: true);
            await using (db)
            await using (connection)
            {
                var entitlementService = new MedicalImagingEntitlementService(db);
                var featureService = new FeatureService(db, tenantContext, entitlementService);
                var departmentResolver = new DepartmentFeatureResolver(db, entitlementService);
                var userFeatureAccess = new UserDepartmentFeatureAccessService(
                    db,
                    tenantContext,
                    departmentResolver,
                    entitlementService);

                Assert.IsTrue((await featureService.GetEffectiveAsync()).MedicalImagingEnabled);
                Assert.IsTrue(await featureService.IsEnabledAsync("medicalImaging"));
                Assert.IsTrue(await userFeatureAccess.CanUserAccessFeatureAsync(
                    tenantId,
                    Guid.NewGuid(),
                    "medicalImagingEnabled"));
            }
        }
    }

    [TestMethod]
    public async Task MedicalImagingApiFilter_ForbidsUnentitledTenantBeforeActionRuns()
    {
        var tenantContext = new TestTenantContext();
        tenantContext.SetTenant(Guid.NewGuid());
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var executingContext = new ActionExecutingContext(
            actionContext,
            filters,
            new Dictionary<string, object?>(),
            new object());
        var filter = new RequireMedicalImagingEntitlementFilter(
            new FixedMedicalImagingFeatureService(false),
            tenantContext);
        var nextWasCalled = false;

        await filter.OnActionExecutionAsync(executingContext, () =>
        {
            nextWasCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, filters, new object()));
        });

        Assert.IsFalse(nextWasCalled);
        Assert.AreEqual(StatusCodes.Status403Forbidden, (executingContext.Result as ObjectResult)?.StatusCode);
    }

    private static async Task<(AppDbContext Db, SqliteConnection Connection, TestTenantContext TenantContext, Guid TenantId)> CreateTenantAsync(
        TenantProductType? product,
        PlanType plan,
        bool quotesEnabled,
        bool invoicesEnabled,
        SubscriptionStatus subscriptionStatus = SubscriptionStatus.PendingApproval,
        DateTime? trialEndsAt = null,
        DateTime? subscriptionEndsAt = null,
        bool isSuspended = false,
        bool medicalImagingEnabled = false)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext();
        tenantContext.SetTenant(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Business capability test",
            Subdomain = $"cap-{Guid.NewGuid():N}",
            ProductType = product,
            Plan = plan,
            SubscriptionStatus = subscriptionStatus,
            TrialEndsAt = trialEndsAt,
            SubscriptionEndsAt = subscriptionEndsAt,
            IsSuspended = isSuspended,
        });
        db.TenantFeatures.Add(new TenantFeatures
        {
            TenantId = tenantId,
            QuotesEnabled = quotesEnabled,
            InvoicesEnabled = invoicesEnabled,
            MedicalImagingEnabled = medicalImagingEnabled,
        });
        await db.SaveChangesAsync();
        return (db, connection, tenantContext, tenantId);
    }

    private sealed class FixedMedicalImagingFeatureService : IFeatureService
    {
        private readonly bool _isEntitled;

        public FixedMedicalImagingFeatureService(bool isEntitled)
        {
            _isEntitled = isEntitled;
        }

        public Task<TenantFeatures> GetAsync() => Task.FromResult(new TenantFeatures());

        public Task<TenantFeatures> GetEffectiveAsync() => GetAsync();

        public Task<bool> IsEnabledAsync(string feature) => Task.FromResult(_isEntitled);
    }
}