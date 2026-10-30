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
public sealed class InvoicePaymentServiceTests
{
    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; private set; } = tenantId;
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid value) => TenantId = value;
        public void SetUserId(Guid value) => UserId = value;
    }

    [TestMethod]
    public async Task CreateAsync_RecordsManualPaymentAndUpdatesInvoiceStatus()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);

        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Payment test tenant",
            Subdomain = "payment-" + Guid.NewGuid().ToString("N"),
            ProductType = TenantProductType.Business,
            Plan = PlanType.Pro,
            Currency = "ILS"
        });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, FullName = "Test client", Phone = "1" });
        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            TenantId = tenantId,
            ClientId = clientId,
            ClientName = "Test client",
            InvoiceNumber = "INV-PAYMENT-1",
            InvoiceDate = DateTime.UtcNow,
            Amount = 100m,
            Subtotal = 100m,
            TotalAmount = 100m,
            FinalAmountToPay = 100m,
            Status = InvoiceStatus.Pending
        });
        await db.SaveChangesAsync();

        var service = new InvoicePaymentService(db, tenantContext);
        var result = await service.CreateAsync(invoiceId,
            new CreatePaymentRequest(25m, DateTime.UtcNow, "cash", null, null), null);

        Assert.AreEqual(CreateInvoicePaymentOutcome.Created, result.Outcome);
        Assert.IsNotNull(result.Summary);
        Assert.AreEqual(25m, result.Summary.VerifiedPaid);
        Assert.AreEqual(75m, result.Summary.RemainingBalance);
        Assert.AreEqual(1, await db.Payments.CountAsync(payment => payment.InvoiceId == invoiceId));
        Assert.AreEqual(InvoiceStatus.PartiallyPaid,
            (await db.Invoices.SingleAsync(invoice => invoice.Id == invoiceId)).Status);
    }
}
