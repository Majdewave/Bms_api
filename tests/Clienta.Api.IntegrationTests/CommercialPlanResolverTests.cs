using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class CommercialPlanResolverTests
{
    [TestMethod]
    public async Task LegacyPro_UsesExistingPlanProviderWithoutQueryingBusinessCatalog()
    {
        var catalog = new NeverCalledBusinessCatalog();
        var legacyProvider = new PlanProvider();
        var resolver = new CommercialPlanResolver(legacyProvider, catalog);

        var result = await resolver.ResolveAsync(null, PlanType.Pro);
        var existing = legacyProvider.GetPlan(PlanType.Pro);

        Assert.AreEqual(ResolvedTenantProduct.Legacy, result.Product);
        Assert.AreEqual(existing.Plan, result.ExistingPlan!.Plan);
        Assert.AreEqual(existing.UserLimit, result.ExistingPlan.UserLimit);
        Assert.AreEqual(existing.MessageLimit, result.ExistingPlan.MessageLimit);
        Assert.AreEqual(existing.AllowCustomBranding, result.ExistingPlan.AllowCustomBranding);
        Assert.AreEqual(existing.EmailAutomation, result.ExistingPlan.EmailAutomation);
        Assert.AreEqual(existing.Priority, result.ExistingPlan.Priority);
        Assert.AreEqual(existing.Support, result.ExistingPlan.Support);
        Assert.IsNull(result.BusinessPlan);
        Assert.AreEqual(-1, result.ExistingPlan.UserLimit);
        Assert.AreEqual(-1, result.ExistingPlan.MessageLimit);
        Assert.AreEqual(0, catalog.Calls);
    }

    [TestMethod]
    public async Task BusinessPlans_ResolveCatalogMetadataAndAllReleasedCapabilities()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var resolver = new CommercialPlanResolver(new PlanProvider(), fixture.Catalog);

        var expectedCapabilities = Enum.GetValues<BusinessCapability>().ToHashSet();
        var expectedPurchase = new Dictionary<PlanType, bool>
        {
            [PlanType.Trial] = false,
            [PlanType.Basic] = true,
            [PlanType.Pro] = false
        };

        foreach (var (plan, name) in new[]
        {
            (PlanType.Trial, "Business Trial"),
            (PlanType.Basic, "Business Basic"),
            (PlanType.Pro, "Business Pro")
        })
        {
            var result = await resolver.ResolveAsync(TenantProductType.Business, plan);
            Assert.AreEqual(ResolvedTenantProduct.Business, result.Product);
            Assert.IsNull(result.ExistingPlan);
            Assert.IsNotNull(result.BusinessPlan);
            Assert.AreEqual(name, result.BusinessPlan.Configuration.DisplayName);
            Assert.IsTrue(result.BusinessPlan.Configuration.Enabled);
            Assert.AreEqual(expectedPurchase[plan], BusinessCommercialPlanCatalog.IsAvailableForPurchase(plan));
            Assert.AreEqual(expectedPurchase[plan], result.BusinessPlan.Configuration.AvailableForPurchase);
            Assert.IsTrue(expectedCapabilities.SetEquals(result.BusinessPlan.Capabilities));
            Assert.IsNull(result.BusinessPlan.Configuration.MonthlyPrice);
            Assert.IsNull(result.BusinessPlan.Configuration.AnnualPrice);
        }
    }

    [TestMethod]
    public async Task UpdatingBusinessProMetadata_CannotEnablePurchasing()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var updated = await fixture.Catalog.UpdateAsync(
            PlanType.Pro,
            new UpdateBusinessPlanConfigurationRequest("Business Pro", null, null, null, true, 2));

        Assert.IsNotNull(updated);
        Assert.IsFalse(updated.Configuration.AvailableForPurchase);
        Assert.IsFalse(BusinessCommercialPlanCatalog.IsAvailableForPurchase(PlanType.Pro));
    }

    [DataTestMethod]
    [DataRow((int)TenantProductType.Clinic, (int)PlanType.Basic)]
    [DataRow((int)TenantProductType.Clinic, (int)PlanType.Pro)]
    [DataRow((int)TenantProductType.Clinic, (int)PlanType.Trial)]
    public async Task ClinicPlans_UseExistingProviderAndNeverQueryBusinessCatalog(int productValue, int planValue)
    {
        var catalog = new NeverCalledBusinessCatalog();
        var resolver = new CommercialPlanResolver(new PlanProvider(), catalog);

        var result = await resolver.ResolveAsync((TenantProductType)productValue, (PlanType)planValue);

        Assert.AreEqual(ResolvedTenantProduct.Clinic, result.Product);
        Assert.IsNotNull(result.ExistingPlan);
        Assert.IsNull(result.BusinessPlan);
        Assert.AreEqual(0, catalog.Calls);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow(999)]
    public async Task NullOrUnknownProductType_UsesLegacyAndNeverBusiness(int? productValue)
    {
        var catalog = new NeverCalledBusinessCatalog();
        var resolver = new CommercialPlanResolver(new PlanProvider(), catalog);
        TenantProductType? productType = productValue.HasValue
            ? (TenantProductType)productValue.Value
            : null;

        var result = await resolver.ResolveAsync(productType, PlanType.Pro);

        Assert.AreEqual(ResolvedTenantProduct.Legacy, result.Product);
        Assert.IsNotNull(result.ExistingPlan);
        Assert.IsNull(result.BusinessPlan);
        Assert.AreEqual(0, catalog.Calls);
    }

    private sealed class NeverCalledBusinessCatalog : IBusinessCommercialPlanCatalog
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<BusinessCommercialPlanDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new AssertFailedException("Legacy and Clinic resolution must not query the Business catalog.");
        }

        public Task<BusinessCommercialPlanDefinition?> GetAsync(PlanType planType, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new AssertFailedException("Legacy and Clinic resolution must not query the Business catalog.");
        }

        public Task<BusinessCommercialPlanDefinition?> UpdateAsync(PlanType planType, UpdateBusinessPlanConfigurationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new AssertFailedException("Tests must not update the Business catalog.");
        }
    }

    private sealed class CatalogFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public MasterDbContext Db { get; }
        public BusinessCommercialPlanCatalog Catalog { get; }

        private CatalogFixture(SqliteConnection connection, MasterDbContext db)
        {
            _connection = connection;
            Db = db;
            Catalog = new BusinessCommercialPlanCatalog(db);
        }

        public static async Task<CatalogFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MasterDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MasterDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            db.CommercialPlanConfigurations.AddRange(
                Create(PlanType.Trial, "Business Trial", false, 0, now),
                Create(PlanType.Basic, "Business Basic", true, 1, now),
                Create(PlanType.Pro, "Business Pro", false, 2, now));
            await db.SaveChangesAsync();
            return new CatalogFixture(connection, db);
        }

        private static CommercialPlanConfiguration Create(PlanType plan, string name, bool purchasable, int order, DateTime now)
            => new()
            {
                Id = Guid.NewGuid(),
                ProductType = TenantProductType.Business,
                PlanType = plan,
                DisplayName = name,
                Description = null,
                MonthlyPrice = null,
                AnnualPrice = null,
                Enabled = true,
                AvailableForPurchase = purchasable,
                DisplayOrder = order,
                CreatedAt = now,
                UpdatedAt = now
            };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}