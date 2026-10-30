using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class ClientFileArchiveConcurrencyTests
{
    private static string GetLocalConnectionString()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        Assert.IsFalse(
            string.IsNullOrWhiteSpace(connectionString),
            "Local PostgreSQL connection must be explicitly configured.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.AreEqual("localhost", builder.Host);
        Assert.AreEqual(5433, builder.Port);
        Assert.AreEqual("clienta_local", builder.Database);

        return connectionString!;
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        private readonly object _sync = new();
        private readonly HashSet<string> _storedKeys = new();
        private readonly HashSet<string> _deletedKeys = new();

        private readonly TaskCompletionSource<bool> _bothUploaded =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _uploadCount;

        public int UploadCount
        {
            get
            {
                lock (_sync)
                    return _uploadCount;
            }
        }

        public string[] StoredKeys
        {
            get
            {
                lock (_sync)
                    return _storedKeys.ToArray();
            }
        }

        public string[] DeletedKeys
        {
            get
            {
                lock (_sync)
                    return _deletedKeys.ToArray();
            }
        }

        public async Task<string> UploadAsync(
            Stream fileStream,
            string fileName,
            string contentType)
        {
            if (contentType != "application/pdf")
                throw new InvalidOperationException("Expected PDF upload.");

            using var buffer = new MemoryStream();
            await fileStream.CopyToAsync(buffer);

            lock (_sync)
            {
                _storedKeys.Add(fileName);
                _uploadCount++;

                if (_uploadCount == 2)
                    _bothUploaded.TrySetResult(true);
            }

            await _bothUploaded.Task.WaitAsync(
                TimeSpan.FromSeconds(15));

            return fileName;
        }

        public Task<Stream> OpenReadAsync(
            string fileKey,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "OpenReadAsync is not used by this test.");
        }

        public Task DeleteAsync(string fileKey)
        {
            lock (_sync)
            {
                _storedKeys.Remove(fileKey);
                _deletedKeys.Add(fileKey);
            }

            return Task.CompletedTask;
        }
    }

    

    [TestMethod]
    public void LocalPostgresArchiveConcurrency_RequiresLocalDatabase()
    {
        var connectionString = GetLocalConnectionString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(connectionString));
    }

    [TestMethod]
    public async Task LocalPostgresArchiveConcurrency_TwoRequestsReturnSameFile()
    {
        var connectionString = GetLocalConnectionString();

        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var tenantContext = new TestTenantContext(tenantId);

        DbContextOptions<AppDbContext> options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        await using var setupDb = new AppDbContext(options, tenantContext);

        try
        {
            setupDb.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Local Archive Concurrency Test",
                Subdomain = $"archive-{tenantId:N}",
                ProductType = TenantProductType.Business,
                Plan = PlanType.Pro,
                Currency = "ILS"
            });

            setupDb.Clients.Add(new Client
            {
                Id = clientId,
                TenantId = tenantId,
                FullName = "Concurrency Test Client",
                IdNumber = "123456",
                Address = "Local Test",
                Phone = "000"
            });

            setupDb.Users.Add(new User
            {
                Id = userId,
                TenantId = tenantId,
                Email = $"archive-{userId:N}@test.local",
                PasswordHash = "test",
                Role = "Owner"
            });

            setupDb.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                TenantId = tenantId,
                ClientId = clientId,
                ClientName = "Concurrency Test Client",
                InvoiceNumber = $"LOCAL-{invoiceId:N}",
                InvoiceDate = DateTime.UtcNow.Date,
                Status = InvoiceStatus.Pending,
                VatRate = 0m,
                Subtotal = 1000m,
                VatAmount = 0m,
                TotalAmount = 1000m,
                Amount = 1000m,
                FinalAmountToPay = 1000m,
                WithholdingTaxRate = 0m,
                WithholdingTaxAmount = 0m
            });

            setupDb.ExternalAccountingDocuments.Add(
                new ExternalAccountingDocument
                {
                    Id = documentId,
                    TenantId = tenantId,
                    InvoiceId = invoiceId,
                    PaymentId = null,
                    Provider = IntegrationProvider.Rivhit,
                    DocumentType = ExternalAccountingPurpose.Invoice,
                    Status = ExternalAccountingDocumentStatus.Issued,
                    RequestReference = $"local-{documentId:N}",
                    Amount = 1000m
                });

            await setupDb.SaveChangesAsync();

            var storage = new FakeFileStorage();

            async Task<ClientFile> ArchiveAsync()
            {
                await using var db =
                    new AppDbContext(options, new TestTenantContext(tenantId));

                var service = new ClientFileService(db, null!, storage);

                var pdfBytes = System.Text.Encoding.ASCII.GetBytes(
                    "%PDF-1.4\n%Local concurrency test\n");

                await using var pdf = new MemoryStream(pdfBytes);

                return await service.CreateExternalPdfAsync(
                    tenantId,
                    clientId,
                    null,
                    userId,
                    documentId,
                    pdf,
                    pdfBytes.Length,
                    "invoice.pdf",
                    "Concurrency Test Invoice");
            }

            var results = await Task.WhenAll(
                ArchiveAsync(),
                ArchiveAsync());

            Assert.AreEqual(
                results[0].Id,
                results[1].Id,
                "Both requests must return the same archived file.");

            Assert.AreEqual(2, storage.UploadCount);
            Assert.AreEqual(1, storage.DeletedKeys.Length);
            Assert.AreEqual(1, storage.StoredKeys.Length);

            setupDb.ChangeTracker.Clear();

            var savedFiles = await setupDb.ClientFiles
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == tenantId &&
                    x.ExternalAccountingDocumentId == documentId)
                .ToListAsync();

            Assert.AreEqual(1, savedFiles.Count);
            Assert.AreEqual(results[0].Id, savedFiles[0].Id);
            Assert.AreEqual(
                savedFiles[0].StorageKey,
                storage.StoredKeys.Single());
        }
        finally
        {
            setupDb.ChangeTracker.Clear();

            await setupDb.ClientFiles.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId)
                .ExecuteDeleteAsync();

            await setupDb.ExternalAccountingDocuments.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId)
                .ExecuteDeleteAsync();

            await setupDb.Invoices.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId)
                .ExecuteDeleteAsync();

            await setupDb.Clients.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId)
                .ExecuteDeleteAsync();

            await setupDb.Users.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId)
                .ExecuteDeleteAsync();

            await setupDb.Tenants.IgnoreQueryFilters()
                .Where(x => x.Id == tenantId)
                .ExecuteDeleteAsync();
        }
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public TestTenantContext(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public Guid TenantId { get; private set; }

        public Guid? UserId { get; private set; }

        public void SetTenant(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public void SetUserId(Guid userId)
        {
            UserId = userId;
        }

      }
    }
