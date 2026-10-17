using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class BusinessSecurityBoundaryTests
{
    [TestMethod]
    public async Task PaymentConfirmationAccess_IsBusinessOnlyAndIndependentOfEnvironment()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
        var tenantContext = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        var businessId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        db.Tenants.AddRange(
            new Tenant { Id = businessId, Name = "Business", Subdomain = $"business-{Guid.NewGuid():N}", ProductType = TenantProductType.Business, Plan = PlanType.Trial },
            new Tenant { Id = clinicId, Name = "Clinic", Subdomain = $"clinic-{Guid.NewGuid():N}", ProductType = TenantProductType.Clinic, Plan = PlanType.Pro },
            new Tenant { Id = legacyId, Name = "Legacy", Subdomain = $"legacy-{Guid.NewGuid():N}", ProductType = null, Plan = PlanType.Pro });
        await db.SaveChangesAsync();

        var access = new PaymentConfirmationAccessService(db);

        Assert.IsTrue(await access.IsEnabledAsync(businessId));
        Assert.IsFalse(await access.IsEnabledAsync(clinicId));
        Assert.IsFalse(await access.IsEnabledAsync(legacyId));
        Assert.IsFalse(await access.IsEnabledAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task PaymentConfirmationRead_IsTenantScopedForInvoiceIds()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
        var tenantContext = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var invoiceA = Guid.NewGuid();
        var invoiceB = Guid.NewGuid();
        db.Tenants.AddRange(
            new Tenant { Id = tenantA, Name = "Business A", Subdomain = $"business-a-{Guid.NewGuid():N}", ProductType = TenantProductType.Business, Plan = PlanType.Trial },
            new Tenant { Id = tenantB, Name = "Business B", Subdomain = $"business-b-{Guid.NewGuid():N}", ProductType = TenantProductType.Business, Plan = PlanType.Trial });
        db.Invoices.AddRange(
            new Invoice { Id = invoiceA, TenantId = tenantA, ClientId = Guid.NewGuid(), InvoiceNumber = "A-1", ClientName = "A client", FinalAmountToPay = 100m },
            new Invoice { Id = invoiceB, TenantId = tenantB, ClientId = Guid.NewGuid(), InvoiceNumber = "B-1", ClientName = "B client", FinalAmountToPay = 100m });
        await db.SaveChangesAsync();

        tenantContext.SetTenant(tenantA);
        var service = new PaymentConfirmationService(
            db,
            tenantContext,
            new PaymentConfirmationAccessService(db),
            new PaymentConfirmationTokenService(),
            new UnavailableDevelopmentPaymentConfirmationProofStorage(),
            new ConfigurationBuilder().Build());

        Assert.IsNotNull(await service.GetByInvoiceAsync(tenantA, invoiceA, CancellationToken.None));
        Assert.IsNull(await service.GetByInvoiceAsync(tenantA, invoiceB, CancellationToken.None));
    }

    [DataTestMethod]
    [DataRow(true, false, true, false)]
    [DataRow(true, true, true, true)]
    [DataRow(false, false, true, true)]
    [DataRow(true, false, false, false)]
    public void BusinessTeamManagement_RequiresAdminInBusinessButPreservesOtherProducts(
        bool isBusinessTenant,
        bool isAdmin,
        bool hasManageStaffPermission,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            StaffManagementAccessRules.CanManage(isBusinessTenant, isAdmin, hasManageStaffPermission));
    }

    [DataTestMethod]
    [DataRow(true, true, false, true)]
    [DataRow(true, false, false, false)]
    [DataRow(false, true, false, false)]
    [DataRow(false, false, true, true)]
    public void BusinessTeamDirectory_ReadAccess_IsBusinessSpecific(
        bool isBusinessTenant,
        bool isStaff,
        bool hasManageStaffPermission,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            StaffManagementAccessRules.CanReadDirectory(isBusinessTenant, isStaff, hasManageStaffPermission));
    }
}
