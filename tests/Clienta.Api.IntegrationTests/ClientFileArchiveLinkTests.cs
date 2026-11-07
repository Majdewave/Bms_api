using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

/// <summary>
/// Verifies FileResponse exposes ExternalAccountingDocumentId (so the UI can match an archived Rivhit
/// PDF to its document) and that deleting an archived ClientFile resets the document's ArchiveStatus to
/// Pending (allowing re-archive) while preserving the ExternalAccountingDocument audit record.
/// </summary>
[TestClass]
public sealed class ClientFileArchiveLinkTests
{
    private sealed class Ctx : ITenantContext
    {
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private sealed class StubStorage : IFileStorage
    {
        public int DeleteCalls;
        public List<string> DeletedKeys { get; } = [];
        public Task<string> UploadAsync(Stream s, string f, string c) => Task.FromResult(f);
        public Task<Stream> OpenReadAsync(string k, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }));
        public Task DeleteAsync(string k) { DeleteCalls++; DeletedKeys.Add(k); return Task.CompletedTask; }
    }

    private static async Task<(SqliteConnection, AppDbContext, Ctx, Guid tenantId, Guid clientId, Guid documentId, Guid fileId)> NewFixtureAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
        var ctx = new Ctx();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options, ctx);
        await db.Database.EnsureCreatedAsync();

        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.TenantId = tenantId;
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Biz", Subdomain = "b-" + Guid.NewGuid().ToString("N"), ProductType = TenantProductType.Business, Plan = PlanType.Pro, Currency = "ILS" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, FullName = "C", Phone = "1" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "u@test.co", PasswordHash = "x", Role = "admin", FullName = "User" });
        db.Invoices.Add(new Invoice { Id = invoiceId, TenantId = tenantId, ClientId = clientId, ClientName = "C", InvoiceNumber = "INV-1", Status = InvoiceStatus.Paid, TotalAmount = 118m, FinalAmountToPay = 118m, VatRate = 18m, Subtotal = 100m, VatAmount = 18m, Amount = 118m });
        db.ExternalAccountingDocuments.Add(new ExternalAccountingDocument
        {
            Id = documentId, TenantId = tenantId, Provider = IntegrationProvider.Rivhit, InvoiceId = invoiceId,
            DocumentType = ExternalAccountingPurpose.Invoice, Status = ExternalAccountingDocumentStatus.Issued,
            ExternalDocumentType = 1, ExternalDocumentNumber = 2543, ExternalDocumentId = "id-2543",
            RequestReference = "REF-2543", Currency = "ILS", Amount = 118m, ArchiveStatus = AccountingArchiveStatus.Archived,
            ArchiveErrorCode = "PreviousArchiveFailure", ArchiveAttemptedAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.ClientFiles.Add(new ClientFile
        {
            Id = fileId, TenantId = tenantId, ClientId = clientId, FileName = "rivhit-2543.pdf", StoredFileName = "s.pdf",
            FilePath = "k", FileSize = 5, UploadedByUserId = userId, ExternalAccountingDocumentId = documentId,
            StorageProvider = "S3", StorageKey = "accounting/t/c/d/s.pdf"
        });
        db.ExternalAccountingOperations.Add(new ExternalAccountingOperation
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Provider = IntegrationProvider.Rivhit, InvoiceId = invoiceId,
            OperationType = ExternalAccountingOperationType.Receipt, Status = ExternalAccountingOperationStatus.Completed,
            Amount = 118m, Currency = "ILS", PaymentMethod = PaymentMethod.Cash, PaymentDate = DateTime.UtcNow,
            IdempotencyKey = "TEST-IDEMPOTENCY", RequestReference = "TEST-RECEIPT-OP", ExternalDocumentNumber = 894
        });
        await db.SaveChangesAsync();
        return (connection, db, ctx, tenantId, clientId, documentId, fileId);
    }

    [TestMethod]
    public async Task ListAsync_ExposesExternalAccountingDocumentId()
    {
        var (connection, db, ctx, tenantId, clientId, documentId, _) = await NewFixtureAsync();
        await using var _ = connection;
        var storage = new StubStorage();
        var service = new ClientFileService(db, new FakeWebHost(), storage);

        var rows = await service.ListAsync(db.ClientFiles.Where(f => f.ClientId == clientId && f.TenantId == tenantId), tenantId);

        var row = rows.Single();
        Assert.AreEqual(documentId, row.ExternalAccountingDocumentId);
    }

    [TestMethod]
    public async Task DeleteFile_RemovesClientFileAndStorage_AndResetsArchiveToPending()
    {
        var (connection, db, ctx, tenantId, clientId, documentId, fileId) = await NewFixtureAsync();
        await using var _ = connection;
        var storage = new StubStorage();
        var controller = new FilesController(db, ctx, new ClientFileService(db, new FakeWebHost(), storage), NullLogger<FilesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.DeleteFile(clientId, fileId);

        Assert.IsInstanceOfType(result, typeof(NoContentResult));
        Assert.AreEqual(1, storage.DeleteCalls);                 // S3 object removed
        CollectionAssert.AreEqual(new[] { "accounting/t/c/d/s.pdf" }, storage.DeletedKeys);
        Assert.AreEqual(0, await db.ClientFiles.CountAsync());   // ClientFile row removed
        var document = await db.ExternalAccountingDocuments.SingleAsync();
        Assert.AreEqual(AccountingArchiveStatus.Pending, document.ArchiveStatus);   // re-archivable
        Assert.IsNull(document.ArchiveErrorCode);
        Assert.IsNull(document.ArchiveAttemptedAt);
        Assert.AreEqual(ExternalAccountingDocumentStatus.Issued, document.Status);  // audit/issuance preserved
        Assert.AreEqual("id-2543", document.ExternalDocumentId);
        Assert.AreEqual(2543, document.ExternalDocumentNumber);
        Assert.AreEqual(1, await db.Invoices.CountAsync());       // invoice is untouched
        Assert.AreEqual(0, await db.Payments.CountAsync());      // no financial records touched
        Assert.AreEqual(1, await db.ExternalAccountingOperations.CountAsync()); // receipt operation is untouched
        Assert.AreEqual(894, (await db.ExternalAccountingOperations.SingleAsync()).ExternalDocumentNumber);
    }

    [TestMethod]
    public async Task DeleteFile_DoesNotDeleteFileOutsideCurrentTenantScope()
    {
        var (connection, db, ctx, tenantId, clientId, documentId, fileId) = await NewFixtureAsync();
        await using var _ = connection;
        var storage = new StubStorage();
        ctx.TenantId = Guid.NewGuid();
        var controller = new FilesController(db, ctx, new ClientFileService(db, new FakeWebHost(), storage), NullLogger<FilesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.DeleteFile(clientId, fileId);

        Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
        Assert.AreEqual(0, storage.DeleteCalls);
        Assert.AreEqual(1, await db.ClientFiles.IgnoreQueryFilters().CountAsync(file => file.Id == fileId && file.TenantId == tenantId));
        var document = await db.ExternalAccountingDocuments.IgnoreQueryFilters().SingleAsync(item => item.Id == documentId);
        Assert.AreEqual(AccountingArchiveStatus.Archived, document.ArchiveStatus);
    }

    private sealed class FakeWebHost : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "clienta-test-wwwroot");
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
    }
}
