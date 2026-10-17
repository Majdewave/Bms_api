using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
            var features = new FeatureService(db, tenantContext);
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

            Assert.IsTrue(await new DepartmentFeatureResolver(db).IsFeatureEnabledAsync(tenantId, departmentId, "quotesEnabled"));
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
                Assert.IsFalse(await new DepartmentFeatureResolver(db).IsFeatureEnabledAsync(tenantId, null, "quotesEnabled"));
            }
        }
    }

    private static async Task<(AppDbContext Db, SqliteConnection Connection, TestTenantContext TenantContext, Guid TenantId)> CreateTenantAsync(
        TenantProductType? product,
        PlanType plan,
        bool quotesEnabled,
        bool invoicesEnabled)
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
        });
        db.TenantFeatures.Add(new TenantFeatures
        {
            TenantId = tenantId,
            QuotesEnabled = quotesEnabled,
            InvoicesEnabled = invoicesEnabled,
        });
        await db.SaveChangesAsync();
        return (db, connection, tenantContext, tenantId);
    }
}