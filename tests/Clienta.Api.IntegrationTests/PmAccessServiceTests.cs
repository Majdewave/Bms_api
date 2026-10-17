using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class PmAccessServiceTests
{
    [DataTestMethod]
    [DataRow(PlanType.Trial)]
    [DataRow(PlanType.Basic)]
    public async Task BusinessTrialAndBasicHavePmAccessWithoutLegacyFlag(PlanType plan)
    {
        Assert.IsTrue(await ResolveAsync(TenantProductType.Business, plan, pmEnabled: false));
    }

    [TestMethod]
    public async Task BusinessProStillUsesExplicitPmFlag()
    {
        Assert.IsFalse(await ResolveAsync(TenantProductType.Business, PlanType.Pro, pmEnabled: false));
        Assert.IsTrue(await ResolveAsync(TenantProductType.Business, PlanType.Pro, pmEnabled: true));
    }

    [TestMethod]
    public async Task ClinicRetainsExplicitPmFlagBehavior()
    {
        Assert.IsFalse(await ResolveAsync(TenantProductType.Clinic, PlanType.Basic, pmEnabled: false));
        Assert.IsTrue(await ResolveAsync(TenantProductType.Clinic, PlanType.Basic, pmEnabled: true));
    }

    [TestMethod]
    public async Task LegacyProRetainsExplicitPmFlagBehavior()
    {
        Assert.IsFalse(await ResolveAsync(null, PlanType.Pro, pmEnabled: false));
        Assert.IsTrue(await ResolveAsync(null, PlanType.Pro, pmEnabled: true));
    }

    [TestMethod]
    public async Task BusinessEntitlementCannotBeResolvedForAnotherTenant()
    {
        Assert.IsFalse(await ResolveAsync(
            TenantProductType.Business,
            PlanType.Trial,
            pmEnabled: false,
            contextTenantId: Guid.NewGuid()));
    }

    private static async Task<bool> ResolveAsync(
        TenantProductType? productType,
        PlanType plan,
        bool pmEnabled,
        Guid? contextTenantId = null)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);

        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext();
        tenantContext.SetTenant(contextTenantId ?? tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "PM entitlement test",
            Subdomain = $"pm-{Guid.NewGuid():N}",
            ProductType = productType,
            Plan = plan,
        });
        db.TenantFeatures.Add(new TenantFeatures
        {
            TenantId = tenantId,
            PmEnabled = pmEnabled,
        });
        await db.SaveChangesAsync();

        return await new PmAccessService(db, tenantContext).IsPmEnabledAsync(tenantId);
    }
}