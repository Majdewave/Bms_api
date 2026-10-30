using System.Text.Json;
using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class InvoiceDeleteReconciliationTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private sealed class EnabledFeatures : IFeatureService
    {
        public Task<TenantFeatures> GetAsync() => Task.FromResult(new TenantFeatures { InvoicesEnabled = true });
        public Task<TenantFeatures> GetEffectiveAsync() => GetAsync();
        public Task<bool> IsEnabledAsync(string feature) => Task.FromResult(feature == "invoices");
    }

    private sealed class DisabledPmAccess : IPmAccessService
    {
        public Task<bool> IsPmEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Clienta.Api.IntegrationTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public TestTenantContext TenantContext { get; }
        public Guid TenantId { get; }
        public Guid InvoiceId { get; }
        public Guid ClientId { get; }
        public InvoicesController Controller { get; }

        private Fixture(SqliteConnection connection, AppDbContext db, TestTenantContext tenantContext,
            Guid tenantId, Guid invoiceId, Guid clientId)
        {
            _connection = connection;
            Db = db;
            TenantContext = tenantContext;
            TenantId = tenantId;
            InvoiceId = invoiceId;
            ClientId = clientId;
            Controller = new InvoicesController(db, tenantContext, new EnabledFeatures(),
                new DisabledPmAccess(), new TestWebHostEnvironment());
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);

            var tenantContext = new TestTenantContext();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, tenantContext);
            await db.Database.EnsureCreatedAsync();

            var tenantId = Guid.NewGuid();
            var clientId = Guid.NewGuid();
            var invoiceId = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Test tenant", Subdomain = $"test-{Guid.NewGuid():N}" });
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                TenantId = tenantId,
                ClientId = clientId,
                InvoiceNumber = "INV-DELETE-TEST",
                ClientName = "Test client",
                Amount = 100m,
                Subtotal = 100m,
                TotalAmount = 100m,
                FinalAmountToPay = 100m,
                VatRate = 0m,
                VatAmount = 0m,
            });
            await db.SaveChangesAsync();
            tenantContext.SetTenant(tenantId);
            return new Fixture(connection, db, tenantContext, tenantId, invoiceId, clientId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        public async Task AddPaymentAsync(PaymentStatus status, Guid? id = null)
        {
            Db.Payments.Add(new Payment
            {
                Id = id ?? Guid.NewGuid(),
                TenantId = TenantId,
                ClientId = ClientId,
                InvoiceId = InvoiceId,
                Amount = 100m,
                PaymentDate = DateTime.UtcNow,
                PaymentMethod = PaymentMethod.Cash,
                Status = status,
            });
            await Db.SaveChangesAsync();
        }

        public async Task AddDocumentAsync(ExternalAccountingDocumentStatus status, Guid? paymentId = null)
        {
            Db.ExternalAccountingDocuments.Add(new ExternalAccountingDocument
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Provider = IntegrationProvider.Rivhit,
                InvoiceId = InvoiceId,
                PaymentId = paymentId,
                DocumentType = paymentId.HasValue ? ExternalAccountingPurpose.Receipt : ExternalAccountingPurpose.Invoice,
                Status = status,
                RequestReference = $"delete-test-document-{Guid.NewGuid():N}",
                ExternalDocumentId = status == ExternalAccountingDocumentStatus.Issued ? "official-rivhit-id" : null,
                ExternalDocumentNumber = status == ExternalAccountingDocumentStatus.Issued ? 12345 : null,
                IssuedAt = status == ExternalAccountingDocumentStatus.Issued ? DateTime.UtcNow : null,
                Amount = 100m,
            });
            await Db.SaveChangesAsync();
        }

        public async Task AddOperationAsync(ExternalAccountingOperationStatus status)
        {
            Db.ExternalAccountingOperations.Add(new ExternalAccountingOperation
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Provider = IntegrationProvider.Rivhit,
                InvoiceId = InvoiceId,
                IdempotencyKey = $"delete-test-operation-{Guid.NewGuid():N}",
                RequestReference = $"delete-test-operation-{Guid.NewGuid():N}",
                Amount = 100m,
                PaymentDate = DateTime.UtcNow,
                Status = status,
            });
            await Db.SaveChangesAsync();
        }
    }

    [TestMethod]
    public async Task Delete_WhenInvoiceHasNoPayments_Succeeds()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Controller.Delete(fixture.InvoiceId);

        Assert.IsInstanceOfType<OkResult>(result);
        Assert.AreEqual(0, await fixture.Db.Invoices.CountAsync(invoice => invoice.Id == fixture.InvoiceId));
    }

    [TestMethod]
    public async Task Delete_WhenInvoiceHasOnlyCancelledPayments_DeletesPaymentAndInvoice()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddPaymentAsync(PaymentStatus.Cancelled);

        var result = await fixture.Controller.Delete(fixture.InvoiceId);

        Assert.IsInstanceOfType<OkResult>(result);
        Assert.AreEqual(0, await fixture.Db.Payments.CountAsync(payment => payment.InvoiceId == fixture.InvoiceId));
        Assert.AreEqual(0, await fixture.Db.Invoices.CountAsync(invoice => invoice.Id == fixture.InvoiceId));
    }

    [TestMethod]
    public async Task Delete_WhenInvoiceHasVerifiedPayment_ReturnsConflictAndPreservesPayment()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddPaymentAsync(PaymentStatus.Verified);

        var result = await fixture.Controller.Delete(fixture.InvoiceId);

        AssertConflictCode(result, "InvoiceHasPayments");
        Assert.AreEqual(1, await fixture.Db.Invoices.CountAsync(invoice => invoice.Id == fixture.InvoiceId));
        Assert.AreEqual(1, await fixture.Db.Payments.CountAsync(payment => payment.InvoiceId == fixture.InvoiceId));
    }

    [TestMethod]
    public async Task Delete_WhenIssuedRivhitInvoiceExists_ReturnsConflictAndPreservesDocument()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddDocumentAsync(ExternalAccountingDocumentStatus.Issued);

        var result = await fixture.Controller.Delete(fixture.InvoiceId);

        AssertConflictCode(result, "AccountingDocumentDeleteBlocked");
        Assert.AreEqual(1, await fixture.Db.Invoices.CountAsync(invoice => invoice.Id == fixture.InvoiceId));
        Assert.AreEqual(1, await fixture.Db.ExternalAccountingDocuments.CountAsync(document => document.InvoiceId == fixture.InvoiceId));
    }

    [TestMethod]
    public async Task Delete_WhenRivhitReconciliationIsRequired_ReturnsConflictAndPreservesRelatedData()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddPaymentAsync(PaymentStatus.Verified);
        var paymentId = await fixture.Db.Payments.Where(payment => payment.InvoiceId == fixture.InvoiceId)
            .Select(payment => payment.Id).SingleAsync();
        await fixture.AddDocumentAsync(ExternalAccountingDocumentStatus.ReconciliationRequired, paymentId);
        await fixture.AddOperationAsync(ExternalAccountingOperationStatus.ReconciliationRequired);

        var result = await fixture.Controller.Delete(fixture.InvoiceId);

        AssertConflictCode(result, "AccountingDocumentDeleteBlocked");
        Assert.AreEqual(1, await fixture.Db.Invoices.CountAsync(invoice => invoice.Id == fixture.InvoiceId));
        Assert.AreEqual(1, await fixture.Db.Payments.CountAsync(payment => payment.InvoiceId == fixture.InvoiceId));
        Assert.AreEqual(1, await fixture.Db.ExternalAccountingDocuments.CountAsync(document => document.InvoiceId == fixture.InvoiceId));
        Assert.AreEqual(1, await fixture.Db.ExternalAccountingOperations.CountAsync(operation => operation.InvoiceId == fixture.InvoiceId));
    }

    [TestMethod]
    public async Task Delete_WhenOperationIsUnresolvedButDocumentIsAbsent_ReturnsConflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddOperationAsync(ExternalAccountingOperationStatus.Issuing);

        var result = await fixture.Controller.Delete(fixture.InvoiceId);

        AssertConflictCode(result, "AccountingReconciliationRequired");
        Assert.AreEqual(1, await fixture.Db.Invoices.CountAsync(invoice => invoice.Id == fixture.InvoiceId));
        Assert.AreEqual(1, await fixture.Db.ExternalAccountingOperations.CountAsync(operation => operation.InvoiceId == fixture.InvoiceId));
    }

    private static void AssertConflictCode(IActionResult result, string expectedCode)
    {
        var conflict = result as ConflictObjectResult;
        Assert.IsNotNull(conflict, "Expected invoice deletion to return HTTP 409.");
        Assert.AreEqual(409, conflict.StatusCode);
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(conflict.Value));
        Assert.AreEqual(expectedCode, response.RootElement.GetProperty("code").GetString());
    }
}
