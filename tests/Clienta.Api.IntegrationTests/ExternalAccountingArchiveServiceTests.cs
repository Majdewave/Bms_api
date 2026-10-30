using System.Net;
using System.Text;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Clienta.Api.Services.Integrations;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

/// <summary>
/// Focused coverage for the safe archive/retry path (ExternalAccountingArchiveService) for an
/// already-Issued Rivhit document whose ArchiveStatus is Pending/Failed. Verifies it only performs
/// read-only Rivhit Copy + PDF download and stores via ClientFile, and never mutates financial records.
/// </summary>
[TestClass]
public sealed class ExternalAccountingArchiveServiceTests
{
    private sealed class Ctx : ITenantContext
    {
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private sealed class StubIntegrations(string? credential) : ITenantIntegrationService
    {
        public Task<IntegrationStateDto> GetStateAsync(Guid id, IntegrationProvider p, CancellationToken ct) => throw new NotImplementedException();
        public Task<IntegrationUpdateResult> UpdateAsync(Guid id, IntegrationProvider p, UpdateIntegrationRequest r, CancellationToken ct) => throw new NotImplementedException();
        public Task<IntegrationTestResult> TestConnectionAsync(Guid id, IntegrationProvider p, CancellationToken ct) => throw new NotImplementedException();
        public Task<IntegrationMetadataResult> GetMetadataAsync(Guid id, IntegrationProvider p, CancellationToken ct) => throw new NotImplementedException();
        public Task<IntegrationStateDto> DisconnectAsync(Guid id, IntegrationProvider p, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetOperationalCredentialAsync(Guid id, IntegrationProvider p, CancellationToken ct) => Task.FromResult(credential);
    }

    private sealed class StubProvider : IAccountingDocumentProvider
    {
        public int CopyCalls;
        public int InvoiceCopyCalls;
        public int ReceiptCopyCalls;
        public string? CopyLink = "https://api.rivhit.co.il/pdf/FileService.svc/GetDocument/000000123/11111111-1111-1111-1111-111111111111/true";
        public IntegrationProvider Provider => IntegrationProvider.Rivhit;
        public Task<ProviderConnectionResult> TestConnectionAsync(string c, CancellationToken ct) => Task.FromResult(ProviderConnectionResult.Ok());
        public Task<IReadOnlyList<ProviderDocumentType>> GetDocumentTypesAsync(string c, CancellationToken ct) => throw new NotImplementedException();
        public Task<decimal> GetVatRateAsync(string c, CancellationToken ct) => throw new NotImplementedException();
        public Task<ProviderMutationResult> IssueInvoiceAsync(string c, AccountingInvoiceIssueRequest r, CancellationToken ct) => throw new NotImplementedException("must never be called by archive");
        public Task<ProviderReceiptMutationResult> IssueReceiptAsync(string c, AccountingReceiptIssueRequest r, CancellationToken ct) => throw new NotImplementedException("must never be called by archive");
        public Task<ProviderDocumentDetails?> GetDocumentDetailsAsync(string c, int dt, int dn, CancellationToken ct) => throw new NotImplementedException();
        public Task<ProviderRequestRecovery> GetLastRequestAsync(string c, string r, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProviderRequestRecovery>> GetAllRequestsAsync(string c, string r, CancellationToken ct) => throw new NotImplementedException();
        public Task<ProviderDeliveryResult> SendDocumentByEmailAsync(string c, int dt, int dn, string e, CancellationToken ct) => throw new NotImplementedException();
        public Task<ProviderDeliveryResult> SendReceiptByEmailAsync(string c, int rt, int rn, string e, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetDocumentCopyLinkAsync(string c, int dt, int dn, CancellationToken ct) { CopyCalls++; InvoiceCopyCalls++; return Task.FromResult(CopyLink); }
        public Task<string?> GetReceiptCopyLinkAsync(string c, int rt, int rn, CancellationToken ct) { CopyCalls++; ReceiptCopyCalls++; return Task.FromResult(CopyLink); }
        public Task<IReadOnlyList<ProviderPaymentType>> GetPaymentTypesAsync(string c, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubClientFiles : IClientFileService
    {
        public int CreateCalls;
        public ClientFile? CreatedFile;
        public string? ValidateUpload(IFormFile? file) => null;
        public bool TryNormalizeTitle(string? raw, out string? title) { title = raw; return true; }
        public Task<ClientFile> CreateAsync(Guid t, Guid c, Guid? p, Guid u, IFormFile f, string? ti) => throw new NotImplementedException();
        public Task<ClientFile> CreateExternalPdfAsync(Guid tenantId, Guid clientId, Guid? projectId, Guid userId, Guid externalAccountingDocumentId, Stream pdfStream, long fileSize, string fileName, string title, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            CreatedFile = new ClientFile { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, FileName = fileName, FileSize = fileSize, Title = title, ExternalAccountingDocumentId = externalAccountingDocumentId, StorageProvider = "S3", StorageKey = "k" };
            return Task.FromResult(CreatedFile);
        }
        public Task<List<FileResponse>> ListAsync(IQueryable<ClientFile> q, Guid t) => throw new NotImplementedException();
        public Task<Stream?> OpenReadAsync(ClientFile f, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(ClientFile f) => throw new NotImplementedException();
        public string GetContentType(string fileName) => "application/pdf";
    }

    private sealed class PdfHandler : HttpMessageHandler
    {
        public int GetCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            GetCalls++;
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.4 fake body");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            return Task.FromResult(response);
        }
    }

    private static async Task<(SqliteConnection, AppDbContext, Guid tenantId, Guid documentId)> NewFixtureAsync(
        ExternalAccountingDocumentStatus status,
        AccountingArchiveStatus archiveStatus,
        ExternalAccountingPurpose documentType = ExternalAccountingPurpose.Invoice)
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
        var paymentId = Guid.NewGuid();
        ctx.TenantId = tenantId;
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Biz", Subdomain = "b-" + Guid.NewGuid().ToString("N"), ProductType = TenantProductType.Business, Plan = PlanType.Pro, Currency = "ILS" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, FullName = "C", Phone = "1" });
        db.Invoices.Add(new Invoice { Id = invoiceId, TenantId = tenantId, ClientId = clientId, ClientName = "C", InvoiceNumber = "INV-1", Status = InvoiceStatus.Paid, TotalAmount = 118m, FinalAmountToPay = 118m, VatRate = 18m, Subtotal = 100m, VatAmount = 18m, Amount = 118m });
        if (documentType == ExternalAccountingPurpose.Receipt)
        {
            db.Payments.Add(new Payment
            {
                Id = paymentId,
                TenantId = tenantId,
                ClientId = clientId,
                InvoiceId = invoiceId,
                Amount = 118m,
                PaymentDate = DateTime.UtcNow,
                Status = PaymentStatus.Verified,
            });
        }
        var document = new ExternalAccountingDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Provider = IntegrationProvider.Rivhit, InvoiceId = invoiceId,
            PaymentId = documentType == ExternalAccountingPurpose.Receipt ? paymentId : null,
            DocumentType = documentType, Status = status, ExternalDocumentType = 1,
            ExternalDocumentNumber = 2543, ExternalDocumentId = "identity-2543", ExternalAmount = 118m,
            RequestReference = "REF-2543", Currency = "ILS", Amount = 118m, ArchiveStatus = archiveStatus,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.ExternalAccountingDocuments.Add(document);
        await db.SaveChangesAsync();
        return (connection, db, tenantId, document.Id);
    }

    private static ExternalAccountingArchiveService NewService(AppDbContext db, StubProvider provider, StubClientFiles files, PdfHandler http) =>
        new(db, new StubIntegrations("cred"), provider, files, new HttpClient(http), NullLogger<ExternalAccountingArchiveService>.Instance);

    [TestMethod]
    public async Task Archive_IssuedPending_StoresPdfViaClientFileAndMarksArchived()
    {
        var (connection, db, tenantId, documentId) = await NewFixtureAsync(ExternalAccountingDocumentStatus.Issued, AccountingArchiveStatus.Pending);
        await using var _ = connection;
        var provider = new StubProvider();
        var files = new StubClientFiles();
        var http = new PdfHandler();
        var service = NewService(db, provider, files, http);

        var result = await service.ArchiveAsync(tenantId, documentId, Guid.NewGuid(), default);

        Assert.AreEqual(AccountingArchiveOutcome.Archived, result.Outcome);
        Assert.IsNotNull(result.ClientFileId);
        Assert.AreEqual(1, provider.CopyCalls);   // only read-only Copy
        Assert.AreEqual(1, http.GetCalls);        // only the PDF GET
        Assert.AreEqual(1, files.CreateCalls);    // saved via ClientFile (S3)
        var doc = await db.ExternalAccountingDocuments.SingleAsync();
        Assert.AreEqual(AccountingArchiveStatus.Archived, doc.ArchiveStatus);
        Assert.AreEqual(ExternalAccountingDocumentStatus.Issued, doc.Status); // issuance untouched
        Assert.AreEqual(0, await db.Payments.CountAsync());                   // no financial records created
    }

    [TestMethod]
    public async Task Archive_NotIssued_IsRejectedWithoutCopy()
    {
        var (connection, db, tenantId, documentId) = await NewFixtureAsync(ExternalAccountingDocumentStatus.ReconciliationRequired, AccountingArchiveStatus.Pending);
        await using var _ = connection;
        var provider = new StubProvider();
        var files = new StubClientFiles();
        var http = new PdfHandler();
        var service = NewService(db, provider, files, http);

        var result = await service.ArchiveAsync(tenantId, documentId, Guid.NewGuid(), default);

        Assert.AreEqual(AccountingArchiveOutcome.DocumentNotIssued, result.Outcome);
        Assert.AreEqual(0, provider.CopyCalls);
        Assert.AreEqual(0, http.GetCalls);
        Assert.AreEqual(0, files.CreateCalls);
    }

    [TestMethod]
    public async Task Archive_FailedStatus_CanBeRetried()
    {
        var (connection, db, tenantId, documentId) = await NewFixtureAsync(ExternalAccountingDocumentStatus.Issued, AccountingArchiveStatus.Failed);
        await using var _ = connection;
        var provider = new StubProvider();
        var files = new StubClientFiles();
        var http = new PdfHandler();
        var service = NewService(db, provider, files, http);

        var result = await service.ArchiveAsync(tenantId, documentId, Guid.NewGuid(), default);

        Assert.AreEqual(AccountingArchiveOutcome.Archived, result.Outcome);
        Assert.AreEqual(1, provider.CopyCalls);
        Assert.AreEqual(AccountingArchiveStatus.Archived, (await db.ExternalAccountingDocuments.SingleAsync()).ArchiveStatus);
    }

    [DataTestMethod]
    [DataRow(AccountingArchiveStatus.Pending)]
    [DataRow(AccountingArchiveStatus.Failed)]
    public async Task Archive_IssuedReceiptPendingOrFailed_UsesReceiptCopyAndStoresLinkedPdf(AccountingArchiveStatus archiveStatus)
    {
        var (connection, db, tenantId, documentId) = await NewFixtureAsync(
            ExternalAccountingDocumentStatus.Issued,
            archiveStatus,
            ExternalAccountingPurpose.Receipt);
        await using var _ = connection;
        var provider = new StubProvider();
        var files = new StubClientFiles();
        var http = new PdfHandler();
        var service = NewService(db, provider, files, http);

        var result = await service.ArchiveAsync(tenantId, documentId, Guid.NewGuid(), default);

        Assert.AreEqual(AccountingArchiveOutcome.Archived, result.Outcome);
        Assert.AreEqual(1, provider.ReceiptCopyCalls, "Receipt archiving must use Receipt.Copy.");
        Assert.AreEqual(0, provider.InvoiceCopyCalls);
        Assert.AreEqual(1, http.GetCalls);
        Assert.AreEqual(1, files.CreateCalls);
        Assert.AreEqual(documentId, files.CreatedFile!.ExternalAccountingDocumentId);
        Assert.AreEqual("S3", files.CreatedFile.StorageProvider);
        Assert.AreEqual(AccountingArchiveStatus.Archived, (await db.ExternalAccountingDocuments.SingleAsync()).ArchiveStatus);
        Assert.AreEqual(1, await db.Payments.CountAsync());
    }
}
