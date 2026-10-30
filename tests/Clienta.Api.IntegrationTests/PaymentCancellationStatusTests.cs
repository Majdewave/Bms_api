using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class PaymentCancellationStatusTests
{
    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; private set; } = tenantId;
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid value) => TenantId = value;
        public void SetUserId(Guid value) => UserId = value;
    }

    private sealed class EnabledFeatureService : IFeatureService
    {
        public Task<TenantFeatures> GetAsync() => throw new NotImplementedException();
        public Task<TenantFeatures> GetEffectiveAsync() => throw new NotImplementedException();
        public Task<bool> IsEnabledAsync(string feature) => Task.FromResult(true);
    }

    [TestMethod]
    public async Task CancelPayment_RecalculatesInvoiceFromRemainingVerifiedPayments()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);

        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var firstPaymentId = Guid.NewGuid();
        var secondPaymentId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Payment cancellation test tenant",
            Subdomain = "payment-cancel-" + Guid.NewGuid().ToString("N"),
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
            InvoiceNumber = "INV-CANCEL-1",
            InvoiceDate = DateTime.UtcNow,
            Amount = 100m,
            Subtotal = 100m,
            TotalAmount = 100m,
            FinalAmountToPay = 100m,
            Status = InvoiceStatus.Paid
        });
        db.Payments.AddRange(
            new Payment
            {
                Id = firstPaymentId,
                TenantId = tenantId,
                ClientId = clientId,
                InvoiceId = invoiceId,
                Amount = 40m,
                PaymentDate = DateTime.UtcNow,
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentStatus.Verified
            },
            new Payment
            {
                Id = secondPaymentId,
                TenantId = tenantId,
                ClientId = clientId,
                InvoiceId = invoiceId,
                Amount = 60m,
                PaymentDate = DateTime.UtcNow,
                PaymentMethod = PaymentMethod.BankTransfer,
                Status = PaymentStatus.Verified
            });
        await db.SaveChangesAsync();

        var controller = new PaymentsController(db, tenantContext, new EnabledFeatureService(), new InvoicePaymentService(db, tenantContext));

        var firstCancellation = await controller.CancelPayment(firstPaymentId);
        Assert.IsInstanceOfType<OkObjectResult>(firstCancellation);
        Assert.AreEqual(InvoiceStatus.PartiallyPaid, (await db.Invoices.SingleAsync(invoice => invoice.Id == invoiceId)).Status);
        Assert.AreEqual(PaymentStatus.Cancelled, (await db.Payments.SingleAsync(payment => payment.Id == firstPaymentId)).Status);

        var secondCancellation = await controller.CancelPayment(secondPaymentId);
        Assert.IsInstanceOfType<OkObjectResult>(secondCancellation);
        Assert.AreEqual(InvoiceStatus.Pending, (await db.Invoices.SingleAsync(invoice => invoice.Id == invoiceId)).Status);
        Assert.AreEqual(PaymentStatus.Cancelled, (await db.Payments.SingleAsync(payment => payment.Id == secondPaymentId)).Status);
    }
}
