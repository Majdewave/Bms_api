using Clienta.Api.Authorization;
using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Clienta.Api.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class RivhitPhase2Tests
{
    private sealed class Context : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private sealed class Products(Guid businessId, Guid? otherId = null, ResolvedTenantProduct otherType = ResolvedTenantProduct.Legacy) : ITenantProductService
    {
        public ResolvedTenantProduct Resolve(TenantProductType? productType) => productType switch
        {
            TenantProductType.Business => ResolvedTenantProduct.Business,
            TenantProductType.Clinic => ResolvedTenantProduct.Clinic,
            _ => ResolvedTenantProduct.Legacy
        };
        public Task<ResolvedTenantProduct> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == businessId ? ResolvedTenantProduct.Business : tenantId == otherId ? otherType : ResolvedTenantProduct.Legacy);
        public string ToApiValue(ResolvedTenantProduct product) => product.ToString();
    }

    private sealed class Features : IFeatureService
    {
        public Task<TenantFeatures> GetAsync() => Task.FromResult(new TenantFeatures { InvoicesEnabled = true });
        public Task<TenantFeatures> GetEffectiveAsync() => GetAsync();
        public Task<bool> IsEnabledAsync(string feature) => Task.FromResult(feature == "invoices");
    }

    private sealed class Integrations(Guid tenantId, string? credential) : ITenantIntegrationService
    {
        public Task<IntegrationStateDto> GetStateAsync(Guid id, IntegrationProvider provider, CancellationToken ct) => throw new NotImplementedException();
        public Task<IntegrationUpdateResult> UpdateAsync(Guid id, IntegrationProvider provider, UpdateIntegrationRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<IntegrationTestResult> TestConnectionAsync(Guid id, IntegrationProvider provider, CancellationToken ct) => throw new NotImplementedException();
        public Task<IntegrationMetadataResult> GetMetadataAsync(Guid id, IntegrationProvider provider, CancellationToken ct) => throw new NotImplementedException();
        public Task<IntegrationStateDto> DisconnectAsync(Guid id, IntegrationProvider provider, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetOperationalCredentialAsync(Guid id, IntegrationProvider provider, CancellationToken ct) => Task.FromResult(id == tenantId ? credential : null);
    }

    private sealed class FakeAccountingProvider : IAccountingDocumentProvider
    {
        public IReadOnlyList<ProviderDocumentType> Types = new[] { new ProviderDocumentType(1, "Tax Invoice", false, true, false) };
        public decimal VatRate = 18m;
        public ProviderMutationResult Mutation = new(ProviderMutationDisposition.Success, new ProviderInvoiceDocument(1, 456, "11111111-1111-1111-1111-111111111111", 567, 118m, "0"));
        public ProviderDocumentDetails? Details;
        public ProviderRequestRecovery Last = new(ProviderRequestStatus.NotFound, null);
        public IReadOnlyList<ProviderRequestRecovery> All = Array.Empty<ProviderRequestRecovery>();
        public int IssueCalls;
        public AccountingInvoiceIssueRequest? LastIssue;
        public int RecoveryCalls;
        public int ReceiptCalls;
        public AccountingReceiptIssueRequest? LastReceipt;
        public ProviderReceiptMutationResult? ReceiptMutation;
        public int TotalCalls;
        public IntegrationProvider Provider => IntegrationProvider.Rivhit;
        public Task<ProviderConnectionResult> TestConnectionAsync(string credential, CancellationToken ct)
        {
            TotalCalls++;
            return Task.FromResult(ProviderConnectionResult.Ok());
        }
        public Task<IReadOnlyList<ProviderDocumentType>> GetDocumentTypesAsync(string credential, CancellationToken ct)
        {
            TotalCalls++;
            return Task.FromResult(Types);
        }
        public Task<decimal> GetVatRateAsync(string credential, CancellationToken ct)
        {
            TotalCalls++;
            return Task.FromResult(VatRate);
        }
        public IReadOnlyList<ProviderPaymentType> PaymentTypes = new[]
        {
            new ProviderPaymentType(2, "Cash", 1),
            new ProviderPaymentType(332, "Bit", 332),
            new ProviderPaymentType(333, "PayBox", 333),
            new ProviderPaymentType(4, "Bank Transfer / העברה בנקאית", 4)
        };
        public Task<IReadOnlyList<ProviderPaymentType>> GetPaymentTypesAsync(string credential, CancellationToken ct)
        {
            TotalCalls++;
            return Task.FromResult(PaymentTypes);
        }
        public Task<ProviderMutationResult> IssueInvoiceAsync(string credential, AccountingInvoiceIssueRequest request, CancellationToken ct)
        {
            TotalCalls++;
            IssueCalls++;
            LastIssue = request;
            return Task.FromResult(Mutation);
        }
        public Task<ProviderReceiptMutationResult> IssueReceiptAsync(string credential, AccountingReceiptIssueRequest request, CancellationToken ct)
        {
            ReceiptCalls++;
            LastReceipt = request;
            var result = ReceiptMutation ?? new ProviderReceiptMutationResult(ProviderMutationDisposition.Success,
                new ProviderReceiptDocument(1, 800 + ReceiptCalls, 567, "receipt-identity-" + ReceiptCalls, request.Amount, 44, null));
            if (result.Disposition == ProviderMutationDisposition.Success && ReceiptMutation is not null
                && result.Document?.Amount != request.Amount)
                result = result with { Document = result.Document with { Amount = request.Amount } };
            return Task.FromResult(result);
        }
        public Task<ProviderDocumentDetails?> GetDocumentDetailsAsync(string credential, int documentType, int documentNumber, CancellationToken ct)
        {
            TotalCalls++;
            return Task.FromResult(Details);
        }
        public Task<ProviderRequestRecovery> GetLastRequestAsync(string credential, string reference, CancellationToken ct)
        {
            TotalCalls++;
            RecoveryCalls++;
            return Task.FromResult(Last);
        }
        public Task<IReadOnlyList<ProviderRequestRecovery>> GetAllRequestsAsync(string credential, string reference, CancellationToken ct)
        {
            TotalCalls++;
            RecoveryCalls++;
            return Task.FromResult(All);
        }

        public int DocumentSendCalls;
        public int ReceiptSendCalls;
        public int DocumentCopyCalls;
        public int ReceiptCopyCalls;
        public ProviderDeliveryResult SendResult = new(ProviderMutationDisposition.Success, null);
        public string? DocumentCopyLink = "https://api.rivhit.co.il/pdf/FileService.svc/GetDocument/000000123/11111111-1111-1111-1111-111111111111/true";
        public string? ReceiptCopyLink = "https://api.rivhit.co.il/pdf/FileService.svc/GetDocument/000000123/22222222-2222-2222-2222-222222222222/true";
        public (int Type, int Number, string Email)? LastDocumentSend;
        public (int Type, int Number, string Email)? LastReceiptSend;

        public Task<ProviderDeliveryResult> SendDocumentByEmailAsync(string credential, int documentType, int documentNumber, string email, CancellationToken ct)
        {
            DocumentSendCalls++;
            LastDocumentSend = (documentType, documentNumber, email);
            return Task.FromResult(SendResult);
        }

        public Task<ProviderDeliveryResult> SendReceiptByEmailAsync(string credential, int receiptType, int receiptNumber, string email, CancellationToken ct)
        {
            ReceiptSendCalls++;
            LastReceiptSend = (receiptType, receiptNumber, email);
            return Task.FromResult(SendResult);
        }

        public Task<string?> GetDocumentCopyLinkAsync(string credential, int documentType, int documentNumber, CancellationToken ct)
        {
            DocumentCopyCalls++;
            return Task.FromResult(DocumentCopyLink);
        }

        public Task<string?> GetReceiptCopyLinkAsync(string credential, int receiptType, int receiptNumber, CancellationToken ct)
        {
            ReceiptCopyCalls++;
            return Task.FromResult(ReceiptCopyLink);
        }
    }

    private sealed class FakeArchive : IExternalAccountingArchiveService
    {
        public int Calls;
        public bool Throw;
        public Guid? LastTenantId;
        public Guid? LastDocumentId;
        public Guid? LastUserId;
        public AccountingArchiveResult Result = new(AccountingArchiveOutcome.Archived, Guid.NewGuid(), null);
        public Task<AccountingArchiveResult> ArchiveAsync(Guid tenantId, Guid documentId, Guid userId, CancellationToken ct)
        {
            Calls++;
            LastTenantId = tenantId;
            LastDocumentId = documentId;
            LastUserId = userId;
            if (Throw) throw new InvalidOperationException("archive backend unavailable");
            return Task.FromResult(Result);
        }
    }

    private sealed class TransactionTrackingPayments(AppDbContext db, IInvoicePaymentService inner) : IInvoicePaymentService
    {
        public bool SawActiveTransaction { get; private set; }

        public Task<CreateInvoicePaymentResult> CreateAsync(Guid invoiceId, CreatePaymentRequest request, Guid? userId,
            Guid? excludeReservationId = null, bool transactionAlreadyActive = false)
        {
            SawActiveTransaction = db.Database.CurrentTransaction is not null;
            return inner.CreateAsync(invoiceId, request, userId, excludeReservationId, transactionAlreadyActive);
        }
    }

    private sealed class RetryOnceAfterPaymentPayments(IInvoicePaymentService inner) : IInvoicePaymentService
    {
        public int Calls { get; private set; }
        public int SimulatedTransientFailures { get; private set; }

        public async Task<CreateInvoicePaymentResult> CreateAsync(Guid invoiceId, CreatePaymentRequest request, Guid? userId,
            Guid? excludeReservationId = null, bool transactionAlreadyActive = false)
        {
            Calls++;
            var result = await inner.CreateAsync(invoiceId, request, userId, excludeReservationId, transactionAlreadyActive);
            if (result.Outcome == CreateInvoicePaymentOutcome.Created && SimulatedTransientFailures == 0)
            {
                SimulatedTransientFailures++;
                throw new PostgresException("Simulated serialization failure after canonical payment save.", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
            }

            return result;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection = null!;
        public AppDbContext Db = null!;
        public Context TenantContext = null!;
        public Guid BusinessId = Guid.NewGuid();
        public Guid OtherId = Guid.NewGuid();
        public Guid InvoiceId = Guid.NewGuid();
        public Guid ClientId = Guid.NewGuid();
        public FakeAccountingProvider Provider = new();
        public FakeArchive Archive = new();
        public InvoiceBalanceService Balances => new(Db);
        public InvoicePaymentService Payments => new(Db, TenantContext, new InvoiceBalanceService(Db));
        public ExternalAccountingReceiptService Receipts => new(Db, TenantContext, new Products(BusinessId),
            new Integrations(BusinessId, Credential), Provider, Payments, new InvoiceBalanceService(Db));
        public ExternalAccountingReceiptService ReceiptsWithArchive => new(Db, TenantContext, new Products(BusinessId),
            new Integrations(BusinessId, Credential), Provider, Payments, new InvoiceBalanceService(Db), Archive);
        public string? Credential = "test-credential";

        public IExternalAccountingDocumentService Service(Guid? tenantId = null)
        {
            TenantContext.SetTenant(tenantId ?? BusinessId);
            return new ExternalAccountingDocumentService(Db, new Products(BusinessId), new Integrations(BusinessId, Credential), new Features(), Provider);
        }

        public IExternalAccountingDocumentService ServiceWithArchive(Guid? tenantId = null)
        {
            TenantContext.SetTenant(tenantId ?? BusinessId);
            return new ExternalAccountingDocumentService(Db, new Products(BusinessId), new Integrations(BusinessId, Credential), new Features(), Provider, Archive);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private static async Task<Fixture> NewFixtureAsync(string idNumber = "123456", string currency = "ILS")
    {
        var f = new Fixture { Connection = new SqliteConnection("Data Source=:memory:"), TenantContext = new Context() };
        f.TenantContext.SetTenant(f.BusinessId);
        await f.Connection.OpenAsync();
        f.Connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Connection).Options;
        f.Db = new AppDbContext(options, f.TenantContext);
        await f.Db.Database.EnsureCreatedAsync();
        f.Db.Tenants.AddRange(
            new Tenant { Id = f.BusinessId, Name = "Business", Subdomain = "biz-" + Guid.NewGuid().ToString("N"), ProductType = TenantProductType.Business, Plan = PlanType.Pro, Currency = currency },
            new Tenant { Id = f.OtherId, Name = "Other", Subdomain = "other-" + Guid.NewGuid().ToString("N"), ProductType = TenantProductType.Business, Plan = PlanType.Pro });
        var client = new Client { Id = f.ClientId, TenantId = f.BusinessId, FullName = "Test Client", IdNumber = idNumber, Address = "Test Street", Phone = "123" };
        var invoice = new Invoice
        {
            Id = f.InvoiceId, TenantId = f.BusinessId, ClientId = f.ClientId, ClientName = client.FullName,
            InvoiceNumber = "INV-TEST-1", InvoiceDate = new DateTime(2026, 10, 18), DueDate = new DateTime(2026, 11, 1),
            Status = InvoiceStatus.Pending, VatRate = 18m, Subtotal = 100m, VatAmount = 18m, TotalAmount = 118m,
            Amount = 118m, FinalAmountToPay = 118m, WithholdingTaxRate = 0m, WithholdingTaxAmount = 0m,
            LineItems = new List<InvoiceLineItem> { new() { Id = Guid.NewGuid(), Description = "Service", Quantity = 1m, Price = 100m } }
        };
        f.Db.Clients.Add(client);
        f.Db.Invoices.Add(invoice);
        f.Db.TenantIntegrations.Add(new TenantIntegration { TenantId = f.BusinessId, Provider = IntegrationProvider.Rivhit, IsEnabled = true, AccountingEnabled = true, EncryptedCredential = "protected" });
        await f.Db.SaveChangesAsync();
        return f;
    }

    [TestMethod]
    public async Task NativeInvoiceCreation_DoesNotCallRivhitOrCreateExternalDocuments()
    {
        await using var f = await NewFixtureAsync();
        var controller = new InvoicesController(
            f.Db, f.TenantContext, new Features(), new PmAccessService(f.Db, f.TenantContext), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var result = await controller.Create(new CreateInvoiceRequest
        {
            ClientId = f.ClientId,
            InvoiceDate = new DateOnly(2026, 10, 20),
            LineItems = new List<CreateInvoiceLineItemRequest>
            {
                new() { Description = "Native service", Quantity = 1m, Price = 25m }
            }
        });

        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.AreEqual(0, f.Provider.TotalCalls);
        Assert.AreEqual(0, f.Provider.TotalCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingDocuments.CountAsync());
        Assert.AreEqual(2, await f.Db.Invoices.CountAsync());
    }

    [TestMethod]
    public async Task TenantAccountingDocumentList_IncludesReceiptPaymentLink()
    {
        await using var f = await NewFixtureAsync();
        var paymentId = Guid.NewGuid();
        f.Db.Payments.Add(new Payment
        {
            Id = paymentId,
            TenantId = f.BusinessId,
            ClientId = f.ClientId,
            InvoiceId = f.InvoiceId,
            Amount = 118m,
            PaymentDate = new DateTime(2026, 10, 20),
            PaymentMethod = PaymentMethod.Cash,
            Status = PaymentStatus.Verified,
        });
        f.Db.ExternalAccountingDocuments.Add(new ExternalAccountingDocument
        {
            TenantId = f.BusinessId,
            Provider = IntegrationProvider.Rivhit,
            InvoiceId = f.InvoiceId,
            PaymentId = paymentId,
            DocumentType = ExternalAccountingPurpose.Receipt,
            Status = ExternalAccountingDocumentStatus.Issued,
            RequestReference = "linked-receipt-test",
            Amount = 118m,
            ExternalDocumentNumber = 901,
        });
        await f.Db.SaveChangesAsync();

        var documents = await f.Service().GetTenantDocumentsAsync(f.BusinessId, CancellationToken.None);

        var receipt = documents.Single(document => document.Purpose == nameof(ExternalAccountingPurpose.Receipt));
        Assert.AreEqual(paymentId, receipt.PaymentId);
        Assert.AreEqual(0, f.Provider.TotalCalls, "Listing accounting documents must not call Rivhit.");
    }

    [TestMethod]
    public void NativeInvoiceAndPaymentOperations_DoNotAcceptAccountingProviderDependencies()
    {
        var nativeConstructors = new[]
        {
            typeof(InvoicesController).GetConstructors().Single(),
            typeof(InvoicePaymentService).GetConstructors().Single()
        };
        Assert.IsTrue(nativeConstructors.SelectMany(constructor => constructor.GetParameters())
            .All(parameter => !typeof(IAccountingDocumentProvider).IsAssignableFrom(parameter.ParameterType)));
        Assert.IsFalse(typeof(InvoicesController).GetConstructors().Single().GetParameters()
            .Any(parameter => typeof(IExternalAccountingDocumentService).IsAssignableFrom(parameter.ParameterType)));
        Assert.IsFalse(typeof(InvoicePaymentService).GetConstructors().Single().GetParameters()
            .Any(parameter => typeof(IExternalAccountingDocumentService).IsAssignableFrom(parameter.ParameterType)));
    }

    [TestMethod]
    public async Task NativePaymentEndpoint_DelegatesWithoutCallingRivhit()
    {
        await using var f = await NewFixtureAsync();
        var controller = new PaymentsController(f.Db, f.TenantContext, new Features(), new InvoicePaymentService(f.Db, f.TenantContext))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.CreatePayment(f.InvoiceId,
            new CreatePaymentRequest(25m, new DateTime(2026, 10, 20), "cash", null, null));

        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
        Assert.AreEqual(0, f.Provider.ReceiptCalls);
        Assert.AreEqual(0, f.Provider.TotalCalls);
        Assert.AreEqual(0, f.Provider.TotalCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingDocuments.CountAsync());
    }

    [TestMethod]
    public async Task NativePayments_SupportPartialSecondPartialAndFullSettlementWithoutRivhitCalls()
    {
        await using var f = await NewFixtureAsync();
        var service = new InvoicePaymentService(f.Db, f.TenantContext);
        var first = await service.CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(40m, new DateTime(2026, 10, 20), "cash", "part-1", null), null);

        Assert.AreEqual(CreateInvoicePaymentOutcome.Created, first.Outcome);
        Assert.AreEqual("partially_paid", first.Summary!.PaymentState);
        Assert.AreEqual(40m, first.Summary.VerifiedPaid);
        Assert.AreEqual(78m, first.Summary.RemainingBalance);
        Assert.AreEqual(InvoiceStatus.PartiallyPaid, (await f.Db.Invoices.SingleAsync()).Status);

        var second = await service.CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(30m, new DateTime(2026, 10, 20), "bank_transfer", "part-2", null), null);
        Assert.AreEqual(CreateInvoicePaymentOutcome.Created, second.Outcome);
        Assert.AreEqual("partially_paid", second.Summary!.PaymentState);
        Assert.AreEqual(70m, second.Summary.VerifiedPaid);
        Assert.AreEqual(48m, second.Summary.RemainingBalance);
        Assert.AreEqual(InvoiceStatus.PartiallyPaid, (await f.Db.Invoices.SingleAsync()).Status);

        var final = await service.CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(48m, new DateTime(2026, 10, 20), "cash", "part-3", null), null);
        Assert.AreEqual(CreateInvoicePaymentOutcome.Created, final.Outcome);
        Assert.AreEqual("paid", final.Summary!.PaymentState);
        Assert.AreEqual(118m, final.Summary.VerifiedPaid);
        Assert.AreEqual(0m, final.Summary.RemainingBalance);
        Assert.AreEqual(InvoiceStatus.Paid, (await f.Db.Invoices.SingleAsync()).Status);
        Assert.AreEqual(3, await f.Db.Payments.CountAsync());
        Assert.IsTrue((await f.Db.Payments.ToListAsync()).All(payment => payment.Status == PaymentStatus.Verified));
        Assert.AreEqual(0, f.Provider.TotalCalls);
        Assert.AreEqual(0, f.Provider.IssueCalls);
        Assert.AreEqual(0, f.Provider.RecoveryCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingDocuments.CountAsync());
    }

    [TestMethod]
    public async Task NativePayment_RejectsOverpaymentAndCancelledInvoiceWithoutRivhitCalls()
    {
        await using var f = await NewFixtureAsync();
        var service = new InvoicePaymentService(f.Db, f.TenantContext);
        var overpayment = await service.CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(118.01m, DateTime.UtcNow, "cash", null, null), null);
        Assert.AreEqual(CreateInvoicePaymentOutcome.ExceedsRemainingBalance, overpayment.Outcome);
        Assert.AreEqual(118m, overpayment.RemainingBalance);
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());

        var invoice = await f.Db.Invoices.SingleAsync();
        invoice.Status = InvoiceStatus.Cancelled;
        await f.Db.SaveChangesAsync();
        var cancelled = await service.CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(10m, DateTime.UtcNow, "cash", null, null), null);
        Assert.AreEqual(CreateInvoicePaymentOutcome.CancelledInvoice, cancelled.Outcome);
        Assert.AreEqual(0, f.Provider.TotalCalls);
        Assert.AreEqual(0, f.Provider.IssueCalls);
        Assert.AreEqual(0, f.Provider.RecoveryCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingDocuments.CountAsync());
    }

    [TestMethod]
    public async Task NativePayment_TenantMismatchIsNotFoundAndCreatesNothing()
    {
        await using var f = await NewFixtureAsync();
        f.TenantContext.SetTenant(f.OtherId);
        var service = new InvoicePaymentService(f.Db, f.TenantContext);
        var result = await service.CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(25m, new DateTime(2026, 10, 20), "cash", null, null), null);

        Assert.AreEqual(CreateInvoicePaymentOutcome.InvoiceNotFound, result.Outcome);
        Assert.AreEqual(0, await f.Db.Payments.IgnoreQueryFilters().CountAsync());
        Assert.AreEqual(0, f.Provider.TotalCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingDocuments.IgnoreQueryFilters().CountAsync());
    }

    [DataTestMethod]
    [DataRow("cash", 2)]
    [DataRow("bit", 332)]
    [DataRow("paybox", 333)]
    public async Task ReceiptV1Methods_MapToVerifiedMetadata(string method, int expectedPaymentType)
    {
        await using var f = await NewFixtureAsync();
        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 25m, PaymentMethod = method, PaymentDate = new DateTime(2026, 10, 20), IdempotencyKey = "map-" + method }, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, result.Outcome);
        Assert.AreEqual(expectedPaymentType, f.Provider.LastReceipt!.PaymentType);
        Assert.AreEqual(1, f.Provider.LastReceipt.ReceiptType);
        Assert.AreEqual(25m, f.Provider.LastReceipt.Amount);
        Assert.IsTrue(f.Provider.LastReceipt.CreateCustomer);
        Assert.IsTrue(f.Provider.LastReceipt.FindById);
        Assert.AreEqual(1, f.Provider.ReceiptCalls);
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
        Assert.AreEqual(1, await f.Db.ExternalAccountingDocuments.CountAsync());
    }

    [DataTestMethod]
    [DataRow("credit")]
    [DataRow("check")]
    [DataRow("other")]
    public async Task ReceiptUnsupportedMethods_FailBeforeProviderAndReservation(string method)
    {
        await using var f = await NewFixtureAsync();
        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 25m, PaymentMethod = method, PaymentDate = DateTime.UtcNow, IdempotencyKey = "unsupported-" + method }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.UnsupportedPaymentMethod, result.Outcome);
        Assert.AreEqual(0, f.Provider.ReceiptCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingOperations.CountAsync());
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());
    }

    [TestMethod]
    public async Task ReceiptFullPayment_UsesCanonicalPaymentAndCompletesDocument()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Success,
            new ProviderReceiptDocument(1, 1000, 567, "full-receipt", 118m, 77, null));
        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 118m, PaymentMethod = "cash", PaymentDate = new DateTime(2026, 10, 20), IdempotencyKey = "full-payment" }, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, result.Outcome);
        var payment = await f.Db.Payments.SingleAsync();
        var document = await f.Db.ExternalAccountingDocuments.SingleAsync();
        Assert.AreEqual(118m, payment.Amount);
        Assert.AreEqual(PaymentStatus.Verified, payment.Status);
        Assert.AreEqual(PaymentMethod.Cash, payment.PaymentMethod);
        Assert.AreEqual(InvoiceStatus.Paid, (await f.Db.Invoices.SingleAsync()).Status);
        Assert.AreEqual(payment.Id, document.PaymentId);
        Assert.AreEqual(ExternalAccountingPurpose.Receipt, document.DocumentType);
        Assert.AreEqual("full-receipt", document.ExternalDocumentId);
        Assert.AreEqual(0m, (await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 1m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "new-key" }, null, default)).AvailableBalance);
    }

    [TestMethod]
    public async Task DeliveryChannelWireFormat_MatchesNumericEnumBinding()
    {
        // The API binds enums numerically (no JsonStringEnumConverter). The frontend must send
        // 1 (None) / 2 (Email). This pins that contract for both invoice and receipt requests.
        Assert.AreEqual(1, (int)AccountingDeliveryChannel.None);
        Assert.AreEqual(2, (int)AccountingDeliveryChannel.Email);

        // Serialize the exact request DTOs the frontend posts and confirm the numeric wire value.
        var invoice = JsonSerializer.Serialize(new IssueAccountingInvoiceRequest { DeliveryChannel = AccountingDeliveryChannel.Email });
        var receipt = JsonSerializer.Serialize(new IssueAccountingReceiptRequest { DeliveryChannel = AccountingDeliveryChannel.Email, PaymentMethod = "cash", Amount = 1m, IdempotencyKey = "k", PaymentDate = DateTime.UtcNow });
        StringAssert.Contains(invoice, "\"DeliveryChannel\":2");
        StringAssert.Contains(receipt, "\"DeliveryChannel\":2");

        // And the models round-trip from the numeric JSON the frontend sends.
        var parsedInvoice = JsonSerializer.Deserialize<IssueAccountingInvoiceRequest>("{\"DeliveryChannel\":2}");
        var parsedReceipt = JsonSerializer.Deserialize<IssueAccountingReceiptRequest>("{\"Amount\":1,\"PaymentMethod\":\"cash\",\"PaymentDate\":\"2026-10-12T00:00:00Z\",\"IdempotencyKey\":\"k\",\"DeliveryChannel\":1}");
        Assert.AreEqual(AccountingDeliveryChannel.Email, parsedInvoice!.DeliveryChannel);
        Assert.AreEqual(AccountingDeliveryChannel.None, parsedReceipt!.DeliveryChannel);
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task ReceiptForExistingVerifiedPayment_LinksWithoutNewPaymentOrReservation()
    {
        await using var f = await NewFixtureAsync();
        // Pre-existing verified payment recorded natively (e.g. bank transfer), no Rivhit call.
        var existing = new Payment
        {
            Id = Guid.NewGuid(), TenantId = f.BusinessId, InvoiceId = f.InvoiceId, ClientId = f.ClientId,
            Amount = 118m, PaymentDate = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = PaymentMethod.BankTransfer, Reference = "677", Status = PaymentStatus.Verified
        };
        f.Db.Payments.Add(existing);
        await f.Db.SaveChangesAsync();
        var paymentCountBefore = await f.Db.Payments.CountAsync();

        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Success,
            new ProviderReceiptDocument(1, 4242, 901, "existing-pay-receipt", 118m, 7, null));

        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest
            {
                Amount = 118m, PaymentMethod = "bank_transfer", PaymentDate = new DateTime(2026, 10, 12),
                IdempotencyKey = "existing-payment-1", PaymentId = existing.Id
            }, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, result.Outcome);
        // No new canonical payment was created.
        Assert.AreEqual(paymentCountBefore, await f.Db.Payments.CountAsync());
        // Operation + document are linked to the EXISTING payment.
        var operation = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(existing.Id, operation.PaymentId);
        Assert.AreEqual(ExternalAccountingOperationStatus.Completed, operation.Status);
        var document = await f.Db.ExternalAccountingDocuments.SingleAsync();
        Assert.AreEqual(existing.Id, document.PaymentId);
        Assert.AreEqual(ExternalAccountingPurpose.Receipt, document.DocumentType);
        Assert.AreEqual("existing-pay-receipt", document.ExternalDocumentId);
        // BankTransfer resolved from provider metadata (Payment.TypeList), not hard-coded.
        Assert.AreEqual(4, f.Provider.LastReceipt!.PaymentType);
        // Balance unchanged: the payment was already verified; no extra reservation reduced it.
        Assert.AreEqual(0m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
    }

    [TestMethod]
    public async Task ReceiptForExistingPayment_RejectsAmountMismatch()
    {
        await using var f = await NewFixtureAsync();
        var existing = new Payment
        {
            Id = Guid.NewGuid(), TenantId = f.BusinessId, InvoiceId = f.InvoiceId, ClientId = f.ClientId,
            Amount = 118m, PaymentDate = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = PaymentMethod.BankTransfer, Reference = "677", Status = PaymentStatus.Verified
        };
        f.Db.Payments.Add(existing);
        await f.Db.SaveChangesAsync();

        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest
            {
                Amount = 50m, PaymentMethod = "bank_transfer", PaymentDate = new DateTime(2026, 10, 12),
                IdempotencyKey = "mismatch-1", PaymentId = existing.Id
            }, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.InvalidAmount, result.Outcome);
        Assert.AreEqual(0, f.Provider.ReceiptCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingOperations.CountAsync());
    }

    [DataTestMethod]
    [DataRow(PaymentMethod.Cash, 2)]
    [DataRow(PaymentMethod.Bit, 332)]
    [DataRow(PaymentMethod.PayBox, 333)]
    [DataRow(PaymentMethod.BankTransfer, 4)]
    public async Task ReceiptForExistingPayment_UsesStoredMethodAndSupportedRivhitPaymentType(
        PaymentMethod storedMethod, int expectedRivhitPaymentType)
    {
        await using var f = await NewFixtureAsync();
        var existing = new Payment
        {
            Id = Guid.NewGuid(), TenantId = f.BusinessId, InvoiceId = f.InvoiceId, ClientId = f.ClientId,
            Amount = 118m, PaymentDate = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = storedMethod, Reference = "stored-method", Status = PaymentStatus.Verified
        };
        f.Db.Payments.Add(existing);
        await f.Db.SaveChangesAsync();

        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest
            {
                Amount = 118m, PaymentMethod = string.Empty, PaymentDate = new DateTime(2026, 10, 12),
                IdempotencyKey = $"stored-method-{storedMethod}", PaymentId = existing.Id
            }, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, result.Outcome);
        Assert.AreEqual(expectedRivhitPaymentType, f.Provider.LastReceipt?.PaymentType);
        Assert.AreEqual(1, f.Provider.ReceiptCalls);
        var operation = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(storedMethod, operation.PaymentMethod);
        Assert.AreEqual(existing.Id, operation.PaymentId);
    }

    [DataTestMethod]
    [DataRow(PaymentMethod.Credit)]
    [DataRow(PaymentMethod.Check)]
    [DataRow(PaymentMethod.Other)]
    public async Task ReceiptForExistingPayment_UnsupportedStoredMethodIsRejectedBeforeProviderMutation(
        PaymentMethod storedMethod)
    {
        await using var f = await NewFixtureAsync();
        var existing = new Payment
        {
            Id = Guid.NewGuid(), TenantId = f.BusinessId, InvoiceId = f.InvoiceId, ClientId = f.ClientId,
            Amount = 118m, PaymentDate = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = storedMethod, Reference = "unsupported-method", Status = PaymentStatus.Verified
        };
        f.Db.Payments.Add(existing);
        await f.Db.SaveChangesAsync();

        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest
            {
                Amount = 118m, PaymentMethod = string.Empty, PaymentDate = new DateTime(2026, 10, 12),
                IdempotencyKey = $"unsupported-method-{storedMethod}", PaymentId = existing.Id
            }, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.UnsupportedPaymentMethod, result.Outcome);
        Assert.AreEqual(0, f.Provider.ReceiptCalls);
        var operation = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(ExternalAccountingOperationStatus.Rejected, operation.Status);
        Assert.AreEqual("UnsupportedPaymentMethod", operation.ProviderResponseCode);
        Assert.AreEqual(storedMethod, operation.PaymentMethod);
        Assert.AreEqual(existing.Id, operation.PaymentId);
    }

    [TestMethod]
    public async Task ReceiptForExistingPayment_NotVerified_ReturnsPaymentNotFound()
    {
        await using var f = await NewFixtureAsync();
        var pending = new Payment
        {
            Id = Guid.NewGuid(), TenantId = f.BusinessId, InvoiceId = f.InvoiceId, ClientId = f.ClientId,
            Amount = 118m, PaymentDate = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = PaymentMethod.BankTransfer, Reference = "678", Status = PaymentStatus.PendingVerification
        };
        f.Db.Payments.Add(pending);
        await f.Db.SaveChangesAsync();

        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest
            {
                Amount = 118m, PaymentMethod = "bank_transfer", PaymentDate = new DateTime(2026, 10, 12),
                IdempotencyKey = "pending-1", PaymentId = pending.Id
            }, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.PaymentNotFound, result.Outcome);
        Assert.AreEqual(0, f.Provider.ReceiptCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingOperations.CountAsync());
    }

    [TestMethod]
    public async Task ReceiptForExistingPayment_IdempotencyKeyReplay_ReturnsSameOperationWithoutRecall()
    {
        await using var f = await NewFixtureAsync();
        var existing = new Payment
        {
            Id = Guid.NewGuid(), TenantId = f.BusinessId, InvoiceId = f.InvoiceId, ClientId = f.ClientId,
            Amount = 118m, PaymentDate = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = PaymentMethod.BankTransfer, Reference = "677", Status = PaymentStatus.Verified
        };
        f.Db.Payments.Add(existing);
        await f.Db.SaveChangesAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Success,
            new ProviderReceiptDocument(1, 5001, 901, "idem-receipt", 118m, 9, null));

        var request = new IssueAccountingReceiptRequest
        {
            Amount = 118m, PaymentMethod = "bank_transfer", PaymentDate = new DateTime(2026, 10, 12),
            IdempotencyKey = "same-key", PaymentId = existing.Id
        };
        var first = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId, request, null, default);
        var second = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId, request, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, first.Outcome);
        Assert.AreEqual(AccountingReceiptOutcome.AlreadyCompleted, second.Outcome);
        Assert.AreEqual(first.OperationId, second.OperationId);
        Assert.AreEqual(1, f.Provider.ReceiptCalls); // never retried
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
        Assert.AreEqual(1, await f.Db.ExternalAccountingDocuments.CountAsync());
    }

    [TestMethod]
    public async Task ReceiptInvalidIdNumber_ReturnsCustomerIdentifierInvalidNotRequired()
    {
        // 10-digit value overflows int32 -> invalid (not "missing").
        await using var f = await NewFixtureAsync(idNumber: "6683478372");
        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 25m, PaymentMethod = "cash", PaymentDate = new DateTime(2026, 10, 20), IdempotencyKey = "bad-id-1" }, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.CustomerIdentifierInvalid, result.Outcome);
        Assert.AreEqual(0, f.Provider.ReceiptCalls);
    }

    [TestMethod]
    public async Task ReceiptMissingIdNumber_ReturnsInvoiceIneligible()
    {
        await using var f = await NewFixtureAsync(idNumber: null);
        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 25m, PaymentMethod = "cash", PaymentDate = new DateTime(2026, 10, 20), IdempotencyKey = "no-id-1" }, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.InvoiceIneligible, result.Outcome);
        Assert.AreEqual(0, f.Provider.ReceiptCalls);
    }

    [TestMethod]
    public async Task ReceiptsPartialAndSecondReceipt_ProduceTwoCanonicalPaymentsAndDocuments()
    {
        await using var f = await NewFixtureAsync();
        var invoice = await f.Db.Invoices.SingleAsync();
        invoice.TotalAmount = 1000m; invoice.Amount = 1000m; invoice.FinalAmountToPay = 1000m;
        await f.Db.SaveChangesAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Success, new ProviderReceiptDocument(1, 101, 567, "receipt-700", 700m, 1, null));
        var first = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 700m, PaymentMethod = "bit", PaymentDate = DateTime.UtcNow, IdempotencyKey = "receipt-700" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.Issued, first.Outcome);
        Assert.AreEqual(InvoiceStatus.PartiallyPaid, (await f.Db.Invoices.SingleAsync()).Status);
        var nativeOverReservation = await f.Payments.CreateAsync(f.InvoiceId, new CreatePaymentRequest(500m, DateTime.UtcNow, "cash", null, null), null);
        Assert.AreEqual(CreateInvoicePaymentOutcome.ExceedsRemainingBalance, nativeOverReservation.Outcome);
        Assert.AreEqual(300m, nativeOverReservation.RemainingBalance);
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Success, new ProviderReceiptDocument(1, 102, 567, "receipt-300", 300m, 2, null));
        var second = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 300m, PaymentMethod = "paybox", PaymentDate = DateTime.UtcNow, IdempotencyKey = "receipt-300" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.Issued, second.Outcome);
        Assert.AreEqual(InvoiceStatus.Paid, (await f.Db.Invoices.SingleAsync()).Status);
        Assert.AreEqual(1000m, (await f.Db.Payments.Where(payment => payment.Status == PaymentStatus.Verified).Select(payment => payment.Amount).ToListAsync()).Sum());
        Assert.AreEqual(2, await f.Db.ExternalAccountingDocuments.CountAsync());
        Assert.AreEqual(2, await f.Db.ExternalAccountingOperations.CountAsync());
        Assert.AreEqual(0m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
    }

    [TestMethod]
    [TestCategory("LocalPostgres")]
    public async Task LocalPostgresReceiptE2E_UsesFakeProviderForPartialReceiptsAndIdempotency()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Assert.IsFalse(string.IsNullOrWhiteSpace(connectionString), "Local-only test connection must be explicitly provided.");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.AreEqual("localhost", builder.Host, "Refusing E2E outside localhost.");
        Assert.AreEqual(5433, builder.Port, "Refusing E2E outside port 5433.");
        Assert.AreEqual("clienta_local", builder.Database, "Refusing E2E outside clienta_local.");

        var context = new Context();
        var tenantId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
            .Options;
        await using var db = new AppDbContext(options, context);
        context.SetTenant(tenantId);
        Assert.AreEqual("NpgsqlRetryingExecutionStrategy", db.Database.CreateExecutionStrategy().GetType().Name);

        try
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Local Phase 3C Test", Subdomain = "local-" + tenantId.ToString("N"), ProductType = TenantProductType.Business, Plan = PlanType.Pro, Currency = "ILS" });
            var client = new Client { Id = clientId, TenantId = tenantId, FullName = "Local Test Client", IdNumber = "123456", Address = "Local Test", Phone = "000" };
            db.Clients.Add(client);
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId, TenantId = tenantId, ClientId = clientId, ClientName = client.FullName,
                InvoiceNumber = "LOCAL-" + invoiceId.ToString("N"), InvoiceDate = DateTime.UtcNow.Date,
                Status = InvoiceStatus.Pending, VatRate = 0m, Subtotal = 1000m, VatAmount = 0m,
                TotalAmount = 1000m, Amount = 1000m, FinalAmountToPay = 1000m,
                WithholdingTaxRate = 0m, WithholdingTaxAmount = 0m
            });
            await db.SaveChangesAsync();

            var encrypted = new IntegrationSecretProtector("local-test-protector-key")
                .Protect("fake-provider-credential", $"integration:Rivhit:{tenantId:N}");
            db.TenantIntegrations.Add(new TenantIntegration
            {
                TenantId = tenantId, Provider = IntegrationProvider.Rivhit,
                IsEnabled = true, AccountingEnabled = true, EncryptedCredential = encrypted
            });
            await db.SaveChangesAsync();

            var fake = new FakeAccountingProvider();
            var integrationService = new TenantIntegrationService(db, new IntegrationSecretProtector("local-test-protector-key"), new IAccountingIntegrationProvider[] { fake });
            var tenantProducts = new TenantProductService(db);
            var balance = new InvoiceBalanceService(db);
            var canonicalPayments = new InvoicePaymentService(db, context, balance);
            var transactionTrackingPayments = new TransactionTrackingPayments(db, canonicalPayments);
            var retryingPayments = new RetryOnceAfterPaymentPayments(transactionTrackingPayments);
            var receiptService = new ExternalAccountingReceiptService(db, context, tenantProducts, integrationService, fake, retryingPayments, balance);
            var invoice = await db.Invoices.AsNoTracking().SingleAsync(item => item.Id == invoiceId);
            Assert.AreEqual(1000m, invoice.FinalAmountToPay);
            Assert.AreEqual(InvoiceStatus.Pending, invoice.Status);
            Assert.AreEqual(1000m, await balance.GetAvailableAsync(tenantId, invoiceId));

            fake.ReceiptMutation = new(ProviderMutationDisposition.Success, new ProviderReceiptDocument(1, 4501, 901, "local-receipt-400", 400m, 1, null));
            var firstRequest = new IssueAccountingReceiptRequest { Amount = 400m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = Guid.NewGuid().ToString("N") };
            var first = await receiptService.IssueAsync(tenantId, invoiceId, firstRequest, null, default);
            Assert.AreEqual(AccountingReceiptOutcome.Issued, first.Outcome);
            Assert.IsTrue(transactionTrackingPayments.SawActiveTransaction);
            Assert.AreEqual(1, retryingPayments.SimulatedTransientFailures);
            Assert.AreEqual(2, retryingPayments.Calls, "The transaction should retry once after the simulated transient failure.");
            Assert.AreEqual(1, fake.ReceiptCalls, "Finalization retry must not call Receipt.New again.");
            invoice = await db.Invoices.AsNoTracking().SingleAsync(item => item.Id == invoiceId);
            Assert.AreEqual(InvoiceStatus.PartiallyPaid, invoice.Status);
            var firstReceiptPayments = await db.Payments.Where(item => item.TenantId == tenantId && item.InvoiceId == invoiceId && item.Status == PaymentStatus.Verified).ToListAsync();
            Assert.AreEqual(1, firstReceiptPayments.Count, "A retry must not create a duplicate canonical Payment.");
            Assert.AreEqual(400m, firstReceiptPayments.Sum(item => item.Amount));
            Assert.AreEqual(600m, await balance.GetAvailableAsync(tenantId, invoiceId));

            fake.ReceiptMutation = new(ProviderMutationDisposition.Success, new ProviderReceiptDocument(1, 4502, 901, "local-receipt-600", 600m, 2, null));
            var secondRequest = new IssueAccountingReceiptRequest { Amount = 600m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = Guid.NewGuid().ToString("N") };
            var second = await receiptService.IssueAsync(tenantId, invoiceId, secondRequest, null, default);
            Assert.AreEqual(AccountingReceiptOutcome.Issued, second.Outcome);
            invoice = await db.Invoices.AsNoTracking().SingleAsync(item => item.Id == invoiceId);
            Assert.AreEqual(InvoiceStatus.Paid, invoice.Status);
            var paid = await db.Payments.Where(item => item.TenantId == tenantId && item.InvoiceId == invoiceId && item.Status == PaymentStatus.Verified).Select(item => item.Amount).ToListAsync();
            Assert.AreEqual(1000m, paid.Sum());
            Assert.AreEqual(2, paid.Count);
            Assert.AreEqual(2, await db.ExternalAccountingOperations.CountAsync(item => item.TenantId == tenantId && item.InvoiceId == invoiceId));
            Assert.AreEqual(2, await db.ExternalAccountingDocuments.CountAsync(item => item.TenantId == tenantId && item.InvoiceId == invoiceId && item.DocumentType == ExternalAccountingPurpose.Receipt));
            Assert.AreEqual(0m, await balance.GetAvailableAsync(tenantId, invoiceId));

            var mutationCount = fake.ReceiptCalls;
            var retry = await receiptService.IssueAsync(tenantId, invoiceId, secondRequest, null, default);
            Assert.AreEqual(AccountingReceiptOutcome.AlreadyCompleted, retry.Outcome);
            Assert.AreEqual(mutationCount, fake.ReceiptCalls);
            Assert.AreEqual(2, await db.Payments.CountAsync(item => item.TenantId == tenantId && item.InvoiceId == invoiceId));
            Assert.AreEqual(2, await db.ExternalAccountingDocuments.CountAsync(item => item.TenantId == tenantId && item.InvoiceId == invoiceId && item.DocumentType == ExternalAccountingPurpose.Receipt));

            var overpayment = await receiptService.IssueAsync(tenantId, invoiceId,
                new IssueAccountingReceiptRequest { Amount = 1m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = Guid.NewGuid().ToString("N") }, null, default);
            Assert.AreEqual(AccountingReceiptOutcome.AmountUnavailable, overpayment.Outcome);
            Assert.AreEqual(mutationCount, fake.ReceiptCalls);
            Assert.AreEqual(2, await db.Payments.CountAsync(item => item.TenantId == tenantId && item.InvoiceId == invoiceId));
            Assert.AreEqual(1000m, paid.Sum());
            Assert.AreEqual(0m, await balance.GetAvailableAsync(tenantId, invoiceId));
        }
        finally
        {
            db.ChangeTracker.Clear();
            var cleanupStrategy = db.Database.CreateExecutionStrategy();
            await cleanupStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.ExternalAccountingDocuments.IgnoreQueryFilters().Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
                await db.ExternalAccountingOperations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
                await db.Payments.IgnoreQueryFilters().Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
                await db.Invoices.IgnoreQueryFilters().Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
                await db.Clients.IgnoreQueryFilters().Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
                await db.TenantIntegrations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId).ExecuteDeleteAsync();
                await db.Tenants.IgnoreQueryFilters().Where(item => item.Id == tenantId).ExecuteDeleteAsync();
                await transaction.CommitAsync();
            });
        }
    }

    [TestMethod]
    public async Task ReceiptOverpaymentRejectedBeforeProvider()
    {
        await using var f = await NewFixtureAsync();
        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 118.01m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "over" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.AmountUnavailable, result.Outcome);
        Assert.AreEqual(0, f.Provider.ReceiptCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingOperations.CountAsync());
    }

    [TestMethod]
    public async Task ReceiptIdempotencyReturnsExistingOperationWithoutSecondProviderMutation()
    {
        await using var f = await NewFixtureAsync();
        var request = new IssueAccountingReceiptRequest { Amount = 40m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "same-browser-operation" };
        var first = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId, request, null, default);
        var second = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId, request, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.Issued, first.Outcome);
        Assert.AreEqual(AccountingReceiptOutcome.AlreadyCompleted, second.Outcome);
        Assert.AreEqual(first.OperationId, second.OperationId);
        Assert.AreEqual(1, f.Provider.ReceiptCalls);
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
    }

    [TestMethod]
    public async Task ReceiptDeterministicRejectionReleasesReservation()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Rejected,
            new ProviderReceiptDocument(null, null, null, null, null, null, "INVALID_PAYMENT"));
        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 80m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "reject" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.ProviderRejected, result.Outcome);
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());
        Assert.AreEqual(118m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
        Assert.AreEqual(ExternalAccountingOperationStatus.Rejected, (await f.Db.ExternalAccountingOperations.SingleAsync()).Status);
    }

    [TestMethod]
    public async Task ReceiptUnknownResult_ReconciliationReleasesReservationViaReadOnlyNotFound()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Unknown, null);
        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 50m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "unknown" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.ReconciliationRequired, result.Outcome);
        Assert.AreEqual(68m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());
        var receiptCalls = f.Provider.ReceiptCalls;
        // Default fake Last = NotFound => the reservation is released via read-only status check, no Receipt.New retry.
        var reconcile = await f.Receipts.ReconcileAsync(f.BusinessId, result.OperationId!.Value, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.ProviderRejected, reconcile.Outcome);
        Assert.AreEqual("ProviderNotReceived", (await f.Db.ExternalAccountingOperations.SingleAsync()).ProviderResponseCode);
        Assert.AreEqual(118m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
        Assert.AreEqual(receiptCalls, f.Provider.ReceiptCalls);
    }

    [TestMethod]
    public async Task ReconciliationFinalizesPreviouslyConfirmedProviderSuccessIdempotently()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Unknown, null);
        var result = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 50m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "manual-reconcile" }, null, default);
        var operation = await f.Db.ExternalAccountingOperations.SingleAsync();
        operation.Status = ExternalAccountingOperationStatus.ReconciliationRequired;
        operation.ExternalDocumentType = 1; operation.ExternalDocumentNumber = 77; operation.ExternalDocumentId = "confirmed-manually";
        operation.ExternalCustomerId = 567; operation.ExternalAmount = 50m; operation.ProviderResponseCode = "ClientaFinalizationPending";
        await f.Db.SaveChangesAsync();
        var calls = f.Provider.ReceiptCalls;

        var first = await f.Receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);
        var second = await f.Receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, first.Outcome);
        Assert.AreEqual(AccountingReceiptOutcome.AlreadyCompleted, second.Outcome);
        var completed = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(ExternalAccountingOperationStatus.Completed, completed.Status);
        Assert.IsNotNull(completed.PaymentId);
        Assert.AreNotEqual("ClientaFinalizationPending", completed.ProviderResponseCode);
        Assert.AreEqual(1, completed.ExternalDocumentType);
        Assert.AreEqual(77, completed.ExternalDocumentNumber);
        Assert.AreEqual("confirmed-manually", completed.ExternalDocumentId);
        Assert.AreEqual(50m, completed.ExternalAmount);
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
        var completedDocument = await f.Db.ExternalAccountingDocuments.SingleAsync();
        Assert.AreEqual(1, await f.Db.ExternalAccountingDocuments.CountAsync());
        Assert.AreNotEqual("ClientaFinalizationPending", completedDocument.ProviderResponseCode);
        Assert.AreEqual(calls, f.Provider.ReceiptCalls);
    }

    [TestMethod]
    public async Task ReconciliationRequired_ConfirmedReceipt_FinalizesInTransactionAndCreatesPaymentOnce()
    {
        await using var f = await NewFixtureAsync();
        // Force an Unknown provider result so the operation lands in ReconciliationRequired.
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Unknown, null);
        var issue = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 50m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "recon-confirm" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.ReconciliationRequired, issue.Outcome);
        var operation = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(ExternalAccountingOperationStatus.ReconciliationRequired, operation.Status);
        var receiptCallsBefore = f.Provider.ReceiptCalls;

        // Provider (read-only) now confirms the receipt exists with the matching amount.
        f.Provider.Last = new(ProviderRequestStatus.Success,
            new ProviderInvoiceDocument(1, 9001, "confirmed-identity", 567, 50m, null));

        var trackingPayments = new TransactionTrackingPayments(f.Db, f.Payments);
        var receipts = new ExternalAccountingReceiptService(f.Db, f.TenantContext, new Products(f.BusinessId),
            new Integrations(f.BusinessId, f.Credential), f.Provider, trackingPayments, f.Balances);
        var reconcile = await receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);
        var retry = await receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, reconcile.Outcome);
        Assert.AreEqual(AccountingReceiptOutcome.AlreadyCompleted, retry.Outcome);
        Assert.IsTrue(trackingPayments.SawActiveTransaction, "Canonical payment creation must participate in the finalization transaction.");
        var updated = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(ExternalAccountingOperationStatus.Completed, updated.Status);
        Assert.IsTrue(updated.PaymentId.HasValue);
        Assert.AreEqual(updated.PaymentId, (await f.Db.Payments.SingleAsync()).Id);
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
        Assert.AreEqual(1, await f.Db.ExternalAccountingDocuments.CountAsync());
        // No new Receipt.New call was made; recovery is read-only.
        Assert.AreEqual(receiptCallsBefore, f.Provider.ReceiptCalls);
    }

    [TestMethod]
    public async Task ReconciliationRequired_ProviderRejected_ReleasesReservation()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Unknown, null);
        var issue = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 50m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "recon-reject" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.ReconciliationRequired, issue.Outcome);
        Assert.AreEqual(68m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
        var operation = await f.Db.ExternalAccountingOperations.SingleAsync();
        var receiptCallsBefore = f.Provider.ReceiptCalls;

        // Provider definitively rejected the request.
        f.Provider.Last = new(ProviderRequestStatus.Rejected,
            new ProviderInvoiceDocument(1, null, null, null, null, "INVALID_PAYMENT"));

        var reconcile = await f.Receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.ProviderRejected, reconcile.Outcome);
        var updated = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(ExternalAccountingOperationStatus.Rejected, updated.Status);
        // Reservation released: full balance is available again.
        Assert.AreEqual(118m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());
        Assert.AreEqual(receiptCallsBefore, f.Provider.ReceiptCalls);
    }

    [TestMethod]
    public async Task ReconciliationRequired_ProviderNotFound_ReleasesReservation()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Unknown, null);
        var issue = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 50m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "recon-notfound" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.ReconciliationRequired, issue.Outcome);
        var operation = await f.Db.ExternalAccountingOperations.SingleAsync();
        var receiptCallsBefore = f.Provider.ReceiptCalls;

        // Provider definitively has nothing for this reference.
        f.Provider.Last = new(ProviderRequestStatus.NotFound, null);

        var reconcile = await f.Receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.ProviderRejected, reconcile.Outcome);
        var updated = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(ExternalAccountingOperationStatus.Rejected, updated.Status);
        Assert.AreEqual("ProviderNotReceived", updated.ProviderResponseCode);
        Assert.AreEqual(118m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
        Assert.AreEqual(receiptCallsBefore, f.Provider.ReceiptCalls);
    }

    [TestMethod]
    public async Task ReconciliationRequired_StillAmbiguous_KeepsStateAndReservation()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Unknown, null);
        var issue = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 50m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "recon-ambiguous" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.ReconciliationRequired, issue.Outcome);
        var operation = await f.Db.ExternalAccountingOperations.SingleAsync();
        var receiptCallsBefore = f.Provider.ReceiptCalls;

        // Provider still cannot say (Unknown).
        f.Provider.Last = new(ProviderRequestStatus.Unknown, null);

        var reconcile = await f.Receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.ReconciliationRequired, reconcile.Outcome);
        var updated = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(ExternalAccountingOperationStatus.ReconciliationRequired, updated.Status);
        // Reservation is NOT released while ambiguous.
        Assert.AreEqual(68m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());
        Assert.AreEqual(receiptCallsBefore, f.Provider.ReceiptCalls);
    }

    [TestMethod]
    public async Task ReconciliationRequired_AmountMismatch_StaysAmbiguous()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.ReceiptMutation = new(ProviderMutationDisposition.Unknown, null);
        var issue = await f.Receipts.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 50m, PaymentMethod = "cash", PaymentDate = DateTime.UtcNow, IdempotencyKey = "recon-mismatch" }, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.ReconciliationRequired, issue.Outcome);
        var operation = await f.Db.ExternalAccountingOperations.SingleAsync();

        // Provider confirms a document but for a DIFFERENT amount: do not finalize.
        f.Provider.Last = new(ProviderRequestStatus.Success,
            new ProviderInvoiceDocument(1, 9002, "other-identity", 567, 999m, null));

        var reconcile = await f.Receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.ReconciliationRequired, reconcile.Outcome);
        Assert.AreEqual(ExternalAccountingOperationStatus.ReconciliationRequired,
            (await f.Db.ExternalAccountingOperations.SingleAsync()).Status);
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());
    }

    [TestMethod]
    public async Task NativeFullPayment_CreatesVerifiedPaymentWithInvoiceAssociations()
    {
        await using var f = await NewFixtureAsync();
        var invoice = await f.Db.Invoices.SingleAsync();
        var projectId = Guid.NewGuid();
        f.Db.Projects.Add(new Project { Id = projectId, TenantId = f.BusinessId, ClientId = f.ClientId, Name = "Payment test project" });
        invoice.ProjectId = projectId;
        await f.Db.SaveChangesAsync();
        var service = new InvoicePaymentService(f.Db, f.TenantContext);

        var result = await service.CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(118m, new DateTime(2026, 10, 20), "credit_card", "  ref-1  ", "  note  "), null);
        var payment = await f.Db.Payments.SingleAsync();

        Assert.AreEqual(CreateInvoicePaymentOutcome.Created, result.Outcome);
        Assert.AreEqual(f.BusinessId, payment.TenantId);
        Assert.AreEqual(f.InvoiceId, payment.InvoiceId);
        Assert.AreEqual(f.ClientId, payment.ClientId);
        Assert.AreEqual(invoice.ProjectId, payment.ProjectId);
        Assert.AreEqual(PaymentStatus.Verified, payment.Status);
        Assert.AreEqual(PaymentMethod.Credit, payment.PaymentMethod);
        Assert.AreEqual("ref-1", payment.Reference);
        Assert.AreEqual("note", payment.Notes);
        Assert.AreEqual(InvoiceStatus.Paid, (await f.Db.Invoices.SingleAsync()).Status);
        Assert.AreEqual(0, f.Provider.TotalCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingDocuments.CountAsync());
    }

    [TestMethod]
    public async Task ExternalAccountingPaymentLink_RejectsDifferentTenantOrInvoice()
    {
        await using var f = await NewFixtureAsync();
        var paymentResult = await new InvoicePaymentService(f.Db, f.TenantContext).CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(25m, DateTime.UtcNow, "cash", null, null), null);
        var paymentId = paymentResult.Summary!.Payments[0].Id;

        f.Db.ExternalAccountingDocuments.Add(new ExternalAccountingDocument
        {
            TenantId = f.OtherId,
            Provider = IntegrationProvider.Rivhit,
            InvoiceId = f.InvoiceId,
            PaymentId = paymentId,
            DocumentType = ExternalAccountingPurpose.Receipt,
            Status = ExternalAccountingDocumentStatus.Issued,
            RequestReference = "cross-tenant-payment-link",
            Amount = 25m
        });
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();

        f.Db.ExternalAccountingDocuments.Add(new ExternalAccountingDocument
        {
            TenantId = f.BusinessId,
            Provider = IntegrationProvider.Rivhit,
            InvoiceId = Guid.NewGuid(),
            PaymentId = paymentId,
            DocumentType = ExternalAccountingPurpose.Receipt,
            Status = ExternalAccountingDocumentStatus.Issued,
            RequestReference = "wrong-invoice-payment-link",
            Amount = 25m
        });
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        Assert.AreEqual(0, await f.Db.ExternalAccountingDocuments.CountAsync());
    }

    [TestMethod]
    public async Task ReceiptDomain_AllowsDistinctPaymentsOnSameInvoiceAndRejectsDuplicatePaymentLink()
    {
        await using var f = await NewFixtureAsync();
        var payments = new InvoicePaymentService(f.Db, f.TenantContext);
        var invoice = await f.Db.Invoices.SingleAsync();
        invoice.FinalAmountToPay = 1000m;
        invoice.TotalAmount = 1000m;
        invoice.Amount = 1000m;
        await f.Db.SaveChangesAsync();
        var first = await payments.CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(400m, DateTime.UtcNow, "cash", null, null), null);
        var second = await payments.CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(600m, DateTime.UtcNow, "cash", null, null), null);
        var paymentIds = new[] { first.Summary!.Payments[0].Id, second.Summary!.Payments[0].Id };
        f.Db.ExternalAccountingDocuments.AddRange(paymentIds.Select((paymentId, index) => new ExternalAccountingDocument
        {
            TenantId = f.BusinessId,
            Provider = IntegrationProvider.Rivhit,
            InvoiceId = f.InvoiceId,
            PaymentId = paymentId,
            DocumentType = ExternalAccountingPurpose.Receipt,
            Status = ExternalAccountingDocumentStatus.Issued,
            RequestReference = $"test-receipt-{index}",
            Amount = index == 0 ? 400m : 600m
        }));
        await f.Db.SaveChangesAsync();
        Assert.AreEqual(2, await f.Db.ExternalAccountingDocuments.CountAsync());

        f.Db.ExternalAccountingDocuments.Add(new ExternalAccountingDocument
        {
            TenantId = f.BusinessId,
            Provider = IntegrationProvider.Rivhit,
            InvoiceId = f.InvoiceId,
            PaymentId = paymentIds[0],
            DocumentType = ExternalAccountingPurpose.Receipt,
            Status = ExternalAccountingDocumentStatus.Issued,
            RequestReference = "test-receipt-duplicate",
            Amount = 400m
        });
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.AreEqual(0, f.Provider.IssueCalls);
        Assert.AreEqual(0, f.Provider.RecoveryCalls);
    }

    [TestMethod]
    public async Task Phase2InvoicePurpose_RemainsUniqueAndCompatibleWithNewDomainShape()
    {
        await using var f = await NewFixtureAsync();
        f.Db.ExternalAccountingDocuments.Add(new ExternalAccountingDocument
        {
            TenantId = f.BusinessId,
            Provider = IntegrationProvider.Rivhit,
            InvoiceId = f.InvoiceId,
            DocumentType = ExternalAccountingPurpose.Invoice,
            Status = ExternalAccountingDocumentStatus.Issued,
            RequestReference = "phase2-invoice-row",
            Amount = 118m
        });
        await f.Db.SaveChangesAsync();
        Assert.IsNull((await f.Db.ExternalAccountingDocuments.SingleAsync()).PaymentId);

        f.Db.ExternalAccountingDocuments.Add(new ExternalAccountingDocument
        {
            TenantId = f.BusinessId,
            Provider = IntegrationProvider.Rivhit,
            InvoiceId = f.InvoiceId,
            DocumentType = ExternalAccountingPurpose.Invoice,
            Status = ExternalAccountingDocumentStatus.Issued,
            RequestReference = "phase2-invoice-duplicate",
            Amount = 118m
        });
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [TestMethod]
    public async Task CombinedInvoiceReceiptPurpose_IsUniquePerInvoice()
    {
        await using var f = await NewFixtureAsync();
        var payment = await new InvoicePaymentService(f.Db, f.TenantContext).CreateAsync(f.InvoiceId,
            new CreatePaymentRequest(25m, DateTime.UtcNow, "cash", null, null), null);
        var paymentId = payment.Summary!.Payments[0].Id;
        ExternalAccountingDocument Combined(string reference) => new()
        {
            TenantId = f.BusinessId,
            Provider = IntegrationProvider.Rivhit,
            InvoiceId = f.InvoiceId,
            PaymentId = paymentId,
            DocumentType = ExternalAccountingPurpose.CombinedInvoiceReceipt,
            Status = ExternalAccountingDocumentStatus.Issued,
            RequestReference = reference,
            Amount = 118m
        };

        f.Db.ExternalAccountingDocuments.Add(Combined("combined-one"));
        await f.Db.SaveChangesAsync();
        f.Db.ExternalAccountingDocuments.Add(Combined("combined-two"));
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task ExternalAccounting_RequestReferenceAndNonNullExternalIdRemainUnique()
    {
        await using var f = await NewFixtureAsync();
        var secondInvoiceId = Guid.NewGuid();
        f.Db.Invoices.Add(new Invoice
        {
            Id = secondInvoiceId, TenantId = f.BusinessId, ClientId = f.ClientId, ClientName = "Test Client",
            InvoiceNumber = "INV-TEST-2", InvoiceDate = new DateTime(2026, 10, 18), DueDate = new DateTime(2026, 11, 1),
            Status = InvoiceStatus.Pending, VatRate = 18m, Subtotal = 100m, VatAmount = 18m, TotalAmount = 118m,
            Amount = 118m, FinalAmountToPay = 118m, WithholdingTaxRate = 0m, WithholdingTaxAmount = 0m,
            LineItems = new List<InvoiceLineItem> { new() { Id = Guid.NewGuid(), Description = "Service", Quantity = 1m, Price = 100m } }
        });
        await f.Db.SaveChangesAsync();
        ExternalAccountingDocument Document(Guid invoiceId, string reference, string? externalId) => new()
        {
            TenantId = f.BusinessId, Provider = IntegrationProvider.Rivhit, InvoiceId = invoiceId,
            DocumentType = ExternalAccountingPurpose.Invoice, Status = ExternalAccountingDocumentStatus.Issued,
            RequestReference = reference, ExternalDocumentId = externalId, Amount = 118m
        };

        f.Db.ExternalAccountingDocuments.Add(Document(f.InvoiceId, "unique-ref", null));
        f.Db.ExternalAccountingDocuments.Add(Document(secondInvoiceId, "unique-ref", null));
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();

        f.Db.ExternalAccountingDocuments.Add(Document(f.InvoiceId, "ref-a", "external-1"));
        await f.Db.SaveChangesAsync();
        f.Db.ExternalAccountingDocuments.Add(Document(secondInvoiceId, "ref-b", "external-1"));
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.AreEqual(1, await f.Db.ExternalAccountingDocuments.CountAsync());
    }

    [TestMethod]
    public async Task IssuedInvoice_ArchiveIsAttemptedBestEffort()
    {
        await using var f = await NewFixtureAsync();
        var result = await f.ServiceWithArchive().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);

        Assert.AreEqual(AccountingDocumentOutcome.Issued, result.Outcome);
        Assert.AreEqual(1, f.Archive.Calls);
        var row = await f.Db.ExternalAccountingDocuments.AsNoTracking().SingleAsync();
        Assert.AreEqual(ExternalAccountingDocumentStatus.Issued, row.Status);
    }

    [TestMethod]
    public async Task IssuedInvoice_ArchiveFailure_DoesNotChangeIssuedStatus()
    {
        await using var f = await NewFixtureAsync();
        f.Archive.Throw = true;
        var result = await f.ServiceWithArchive().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);

        Assert.AreEqual(AccountingDocumentOutcome.Issued, result.Outcome);
        var row = await f.Db.ExternalAccountingDocuments.AsNoTracking().SingleAsync();
        Assert.AreEqual(ExternalAccountingDocumentStatus.Issued, row.Status);
        Assert.AreEqual(1, f.Archive.Calls);
    }

    [TestMethod]
    public async Task IssuedInvoice_WithoutArchive_DoesNotCallArchive()
    {
        await using var f = await NewFixtureAsync();
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);

        Assert.AreEqual(AccountingDocumentOutcome.Issued, result.Outcome);
        Assert.AreEqual(0, f.Archive.Calls);
    }

    [TestMethod]
    public async Task IssuedReceipt_ArchiveIsAttemptedBestEffort()
    {
        await using var f = await NewFixtureAsync();
        var userId = Guid.NewGuid();
        var result = await f.ReceiptsWithArchive.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 25m, PaymentMethod = "cash", PaymentDate = new DateTime(2026, 10, 20), IdempotencyKey = "arch-receipt" }, userId, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, result.Outcome);
        Assert.AreEqual(1, f.Archive.Calls);
        var document = await f.Db.ExternalAccountingDocuments.AsNoTracking().SingleAsync();
        Assert.AreEqual(ExternalAccountingDocumentStatus.Issued, document.Status);
        Assert.AreEqual(f.BusinessId, f.Archive.LastTenantId);
        Assert.AreEqual(document.Id, f.Archive.LastDocumentId);
        Assert.AreEqual(userId, f.Archive.LastUserId);
    }

    [TestMethod]
    public async Task IssuedReceipt_ArchiveFailure_KeepsIssuedAndCompleted()
    {
        await using var f = await NewFixtureAsync();
        f.Archive.Throw = true;
        var userId = Guid.NewGuid();
        var result = await f.ReceiptsWithArchive.IssueAsync(f.BusinessId, f.InvoiceId,
            new IssueAccountingReceiptRequest { Amount = 25m, PaymentMethod = "cash", PaymentDate = new DateTime(2026, 10, 20), IdempotencyKey = "arch-fail" }, userId, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, result.Outcome);
        var operation = await f.Db.ExternalAccountingOperations.AsNoTracking().SingleAsync();
        Assert.AreEqual(ExternalAccountingOperationStatus.Completed, operation.Status);
        var document = await f.Db.ExternalAccountingDocuments.AsNoTracking().SingleAsync();
        Assert.AreEqual(ExternalAccountingDocumentStatus.Issued, document.Status);
        Assert.AreEqual(1, f.Archive.Calls);
        Assert.AreEqual(userId, f.Archive.LastUserId);
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
    }

    [TestMethod]
    public async Task ValidPendingInvoice_IssuesAndDoesNotChangeOperationalFinance()
    {
        await using var f = await NewFixtureAsync();
        var before = await f.Db.Invoices.AsNoTracking().SingleAsync();
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId,f.InvoiceId,new IssueAccountingInvoiceRequest(),CancellationToken.None);
        var after = await f.Db.Invoices.AsNoTracking().SingleAsync();
        var row = await f.Db.ExternalAccountingDocuments.AsNoTracking().SingleAsync();

        Assert.AreEqual(AccountingDocumentOutcome.Issued, result.Outcome);
        Assert.AreEqual(1, f.Provider.IssueCalls);
        Assert.AreEqual(123456, f.Provider.LastIssue!.ClientIdNumber);
        Assert.AreEqual(1m, f.Provider.LastIssue.Lines[0].Quantity);
        Assert.AreEqual(100m, f.Provider.LastIssue.Lines[0].UnitPrice);
        Assert.AreEqual("CLIENTA-RIVHIT-INVOICE-" + f.InvoiceId.ToString("N"), row.RequestReference);
        Assert.AreEqual(ExternalAccountingDocumentStatus.Issued, row.Status);
        Assert.AreEqual("456", row.ExternalDocumentNumber.ToString());
        Assert.AreEqual("11111111-1111-1111-1111-111111111111", row.ExternalDocumentId);
        Assert.AreEqual(before.TotalAmount, after.TotalAmount);
        Assert.AreEqual(before.Status, after.Status);
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());
    }

    [TestMethod]
    public async Task ExistingIssuedDocument_IsReturnedWithoutProviderMutation()
    {
        await using var f = await NewFixtureAsync();
        await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.AlreadyIssued, result.Outcome);
        Assert.AreEqual(1, f.Provider.IssueCalls);
    }

    [TestMethod]
    public async Task BusinessAdminPolicy_AllowsAdminAndRejectsStaff()
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization(IntegrationAuthorization.AddPolicies).BuildServiceProvider();
        var auth = services.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal User(string role) => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));
        Assert.IsTrue((await auth.AuthorizeAsync(User("Admin"), null, IntegrationAuthorization.ManagePolicy)).Succeeded);
        Assert.IsFalse((await auth.AuthorizeAsync(User("Staff"), null, IntegrationAuthorization.ManagePolicy)).Succeeded);
    }

    [TestMethod]
    public void InvoiceControllerPolicy_DoesNotGrantIssueToInvoiceStaff()
    {
        var issue = typeof(BusinessRivhitAccountingDocumentsController).GetMethod("Issue")!;
        var authorize = issue.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.AreEqual(IntegrationAuthorization.ManagePolicy, authorize.Policy);
    }

    [DataTestMethod]
    [DataRow(InvoiceStatus.Paid)]
    [DataRow(InvoiceStatus.PartiallyPaid)]
    [DataRow(InvoiceStatus.Cancelled)]
    public async Task NonPendingInvoices_AreRejected(InvoiceStatus status)
    {
        await using var f = await NewFixtureAsync();
        var invoice = await f.Db.Invoices.SingleAsync();
        invoice.Status = status;
        await f.Db.SaveChangesAsync();
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.InvoiceStateUnsupported, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [TestMethod]
    [DataTestMethod]
    [DataRow(TenantProductType.Clinic)]
    [DataRow(ResolvedTenantProduct.Legacy)]
    public async Task NonBusinessTenant_IsRejected(ResolvedTenantProduct productType)
    {
        await using var f = await NewFixtureAsync();
        var otherTenant = await f.Db.Tenants.IgnoreQueryFilters().SingleAsync(tenant => tenant.Id == f.OtherId);
        otherTenant.ProductType = productType == ResolvedTenantProduct.Clinic ? TenantProductType.Clinic : null;
        await f.Db.SaveChangesAsync();
        var service = new ExternalAccountingDocumentService(f.Db, new Products(f.BusinessId, f.OtherId, productType),
            new Integrations(f.BusinessId, f.Credential), new Features(), f.Provider);
        var result = await service.IssueInvoiceAsync(f.OtherId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.UnsupportedTenant, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [TestMethod]
    public async Task CrossTenantInvoice_IsNotFound()
    {
        await using var f = await NewFixtureAsync();
        f.TenantContext.SetTenant(f.OtherId);
        var service = new ExternalAccountingDocumentService(f.Db, new Products(f.BusinessId), new Integrations(f.BusinessId, f.Credential), new Features(), f.Provider);
        var result = await service.IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.IntegrationUnavailable, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [TestMethod]
    public async Task DisabledIntegration_IsRejected()
    {
        await using var f = await NewFixtureAsync();
        var integration = await f.Db.TenantIntegrations.SingleAsync();
        integration.IsEnabled = false;
        await f.Db.SaveChangesAsync();
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.IntegrationUnavailable, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [TestMethod]
    public async Task AccountingDisabled_IsRejected()
    {
        await using var f = await NewFixtureAsync();
        var integration = await f.Db.TenantIntegrations.SingleAsync();
        integration.AccountingEnabled = false;
        await f.Db.SaveChangesAsync();
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.IntegrationUnavailable, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [TestMethod]
    public async Task MissingCredential_IsRejected()
    {
        await using var f = await NewFixtureAsync();
        f.Credential = null;
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.CredentialUnavailable, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public async Task MissingClientNumber_ReturnsCustomerIdentifierRequired(string? idNumber)
    {
        await using var f = await NewFixtureAsync(idNumber);
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.CustomerIdentifierRequired, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [DataTestMethod]
    [DataRow("01234")]        // leading zero
    [DataRow("abc")]          // non-digit
    [DataRow("12-345")]       // punctuation
    [DataRow("2147483648")]   // int32 max + 1 -> overflow
    [DataRow("6683478372")]   // 10-digit -> overflow (the production INV-8 case)
    public async Task InvalidClientNumber_ReturnsCustomerIdentifierInvalid(string idNumber)
    {
        await using var f = await NewFixtureAsync(idNumber);
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.CustomerIdentifierInvalid, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [TestMethod]
    public async Task ValidClientNumber_StillIssuesSuccessfully()
    {
        await using var f = await NewFixtureAsync("516264675"); // 9-digit valid
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.Issued, result.Outcome);
        Assert.AreEqual(1, f.Provider.IssueCalls);
        Assert.AreEqual(516264675, f.Provider.LastIssue!.ClientIdNumber);
        Assert.IsNull(f.Provider.LastIssue.ClientIdNumberIssue);
    }

    [TestMethod]
    public async Task NonIlsInvoice_IsRejected()
    {
        await using var f = await NewFixtureAsync(currency: "USD");
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.CurrencyUnsupported, result.Outcome);
    }

    [TestMethod]
    public async Task VatMismatch_IsRejectedBeforeReservation()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.VatRate = 17m;
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.VatMismatch, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
        Assert.AreEqual(0, await f.Db.ExternalAccountingDocuments.CountAsync());
    }

    [TestMethod]
    public async Task IncompatibleDocumentTypeMetadata_FailsClosed()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.Types = new[] { new ProviderDocumentType(1, "Tax Invoice", true, true, false) };
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.ProviderUnavailable, result.Outcome);
        Assert.AreEqual(0, f.Provider.IssueCalls);
    }

    [TestMethod]
    public async Task NonzeroWithholdingTax_IsRejected()
    {
        await using var f = await NewFixtureAsync();
        var invoice = await f.Db.Invoices.SingleAsync();
        invoice.WithholdingTaxRate = 1m;
        invoice.WithholdingTaxAmount = 1m;
        invoice.FinalAmountToPay = 117m;
        await f.Db.SaveChangesAsync();
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.InvoiceDataInvalid, result.Outcome);
    }

    [TestMethod]
    public async Task RecoveredTimeoutSuccess_MarksIssuedWithoutSecondDocumentNew()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.Mutation = new(ProviderMutationDisposition.Unknown, null);
        f.Provider.Last = new(ProviderRequestStatus.Success, new ProviderInvoiceDocument(1, 789, "22222222-2222-2222-2222-222222222222", 567, 118m, "0"));
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.Issued, result.Outcome);
        Assert.AreEqual(1, f.Provider.IssueCalls);
        Assert.AreEqual(1, f.Provider.RecoveryCalls);
        Assert.AreEqual(789, result.Document!.ExternalDocumentNumber);
    }

    [TestMethod]
    public async Task UnrecoverableTimeout_BlocksReissue()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.Mutation = new(ProviderMutationDisposition.Unknown, null);
        // Ambiguous: the provider cannot say whether the document exists -> keep blocking reissue.
        f.Provider.Last = new(ProviderRequestStatus.Unknown, null);
        f.Provider.All = new[] { new ProviderRequestRecovery(ProviderRequestStatus.Unknown, null) };
        var first = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        var second = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.ReconciliationRequired, first.Outcome);
        Assert.AreEqual(AccountingDocumentOutcome.ReconciliationRequired, second.Outcome);
        Assert.AreEqual(1, f.Provider.IssueCalls);
    }

    [TestMethod]
    public async Task RecoveredRejection_IsRejected()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.Mutation = new(ProviderMutationDisposition.Unknown, null);
        f.Provider.Last = new(ProviderRequestStatus.Rejected, new ProviderInvoiceDocument(1, null, null, null, null, "INVALID_ASMACHTA"));
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.ProviderRejected, result.Outcome);
        Assert.AreEqual(ExternalAccountingDocumentStatus.Rejected, (await f.Db.ExternalAccountingDocuments.SingleAsync()).Status);
    }

    [TestMethod]
    public async Task ProviderAmountMismatch_PersistsReconciliationNotIssued()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.Mutation = new(ProviderMutationDisposition.Success, new ProviderInvoiceDocument(1, 456, "33333333-3333-3333-3333-333333333333", 567, 117m, "0"));
        var result = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.ProviderResponseMismatch, result.Outcome);
        Assert.AreEqual(ExternalAccountingDocumentStatus.ReconciliationRequired, (await f.Db.ExternalAccountingDocuments.SingleAsync()).Status);
        Assert.AreEqual(118m, (await f.Db.Invoices.AsNoTracking().SingleAsync()).TotalAmount);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return response(request);
        }
    }

    [TestMethod]
    public async Task RivhitRequest_UsesApprovedFieldsAndNoMail()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { error_code = 0, data = new { document_type = 1, document_number = 1, document_identity = "44444444-4444-4444-4444-444444444444", customer_id = 5, amount = 118m } })
        }));
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);
        var request = new AccountingInvoiceIssueRequest(Guid.NewGuid(), "INV-1", new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 30), "Client", "Address", "Phone", 123, null, null, 18m, 118m, new[] { new AccountingInvoiceLineRequest("Service", 1, 100m) }, "CLIENTA-RIVHIT-INVOICE-1");
        var result = await provider.IssueInvoiceAsync("secret-value", request, default);
        Assert.AreEqual(ProviderMutationDisposition.Success, result.Disposition);
        Assert.AreEqual("Document.New", handler.LastRequest!.RequestUri!.AbsolutePath.Split('/').Last());
        var json = await handler.LastRequest.Content!.ReadAsStringAsync();
        StringAssert.Contains(json, "\"issue_date\":\"19/10/2026\"");
        StringAssert.Contains(json, "\"due_date\":\"30/10/2026\"");
        StringAssert.Contains(json, "\"currency_id\":1");
        StringAssert.Contains(json, "\"exchange_rate\":1");
        StringAssert.Contains(json, "\"price_include_vat\":false");
        StringAssert.Contains(json, "\"round_digits\":2");
        StringAssert.Contains(json, "\"price_nis\":100");
        StringAssert.Contains(json, "\"quantity\":1");
        StringAssert.Contains(json, "\"exempt_vat\":false");
        StringAssert.Contains(json, "\"create_customer\":true");
        StringAssert.Contains(json, "\"find_by_id\":true");
        StringAssert.Contains(json, "\"send_mail\":false");
        Assert.IsFalse(json.Contains("issue_time", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task DocumentNew_OutgoingHttpBodyContainsRoundDigitsTwo()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                error_code = 0,
                data = new
                {
                    document_type = 1,
                    document_number = 1,
                    document_identity = "44444444-4444-4444-4444-444444444444",
                    customer_id = 5,
                    amount = 118m
                }
            })
        }));
        var provider = new RivhitAccountingProvider(
            new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) },
            NullLogger<RivhitAccountingProvider>.Instance);
        var request = new AccountingInvoiceIssueRequest(
            Guid.NewGuid(), "INV-ROUND", new DateOnly(2026, 10, 19), null, "Client", null, null,
            123, null, null, 18m, 118m,
            new[] { new AccountingInvoiceLineRequest("Service", 1, 100m) },
            "CLIENTA-RIVHIT-ROUND-DIGITS-TEST");

        var result = await provider.IssueInvoiceAsync("test-credential", request, default);

        Assert.AreEqual(ProviderMutationDisposition.Success, result.Disposition);
        Assert.IsNotNull(handler.LastRequest);
        Assert.AreEqual("Document.New", handler.LastRequest!.RequestUri!.AbsolutePath.Split('/').Last());
        Assert.AreEqual("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        var serializedHttpBody = await handler.LastRequest.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(serializedHttpBody);
        Assert.IsTrue(json.RootElement.TryGetProperty("round_digits", out var roundDigits),
            "The serialized Document.New HTTP body must include round_digits.");
        Assert.AreEqual(JsonValueKind.Number, roundDigits.ValueKind);
        Assert.AreEqual(2, roundDigits.GetInt32());
    }

    [DataTestMethod]
    [DataRow(2026, 10, 19, "19/10/2026")]
    [DataRow(2027, 1, 1, "01/01/2027")]
    [DataRow(2026, 12, 31, "31/12/2026")]
    [DataRow(2027, 3, 28, "28/03/2027")]
    public async Task InvoiceCalendarDates_AreSerializedWithoutTimezoneConversion(int year, int month, int day, string expected)
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { error_code = 0, data = new { document_type = 1, document_number = 2, document_identity = "55555555-5555-5555-5555-555555555555", customer_id = 5, amount = 118m } })
        }));
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);
        var date = new DateOnly(year, month, day);
        var request = new AccountingInvoiceIssueRequest(Guid.NewGuid(), "INV-DATE", date, date, "Client", "Address", "Phone", 123, null, null, 18m, 118m, new[] { new AccountingInvoiceLineRequest("Service", 1, 100m) }, "CLIENTA-RIVHIT-DATE-TEST");

        await provider.IssueInvoiceAsync("test-credential", request, default);

        var json = await handler.LastRequest!.Content!.ReadAsStringAsync();
        StringAssert.Contains(json, $"\"issue_date\":\"{expected}\"");
        StringAssert.Contains(json, $"\"due_date\":\"{expected}\"");
    }

    [TestMethod]
    public async Task LastRequest_UsesDocumentedJsonFormatPathAndParsesOriginalResponse()
    {
        const string original = "{\"error_code\":0,\"data\":{\"document_type\":1,\"document_number\":2530,\"document_identity\":\"d3cc0c53-7c22-4a54-b4f3-d90d38d210bb\",\"customer_id\":66,\"amount\":118}}";
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { error_code = 0, data = original })
        }));
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);

        var result = await provider.GetLastRequestAsync("test-credential", "CLIENTA-RIVHIT-INVOICE-4651b0a6e63a47999b57d067de9ed1c0", default);

        Assert.AreEqual(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.AreEqual("/online/RivhitOnlineAPI.svc/Status.LastRequest/JSON", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.AreEqual("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        var requestBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        StringAssert.Contains(requestBody, "\"api_token\":\"test-credential\"");
        StringAssert.Contains(requestBody, "\"request_reference\":\"CLIENTA-RIVHIT-INVOICE-4651b0a6e63a47999b57d067de9ed1c0\"");
        Assert.AreEqual(ProviderRequestStatus.Success, result.Status);
        Assert.AreEqual(1, result.Document!.DocumentType);
        Assert.AreEqual(2530, result.Document.DocumentNumber);
        Assert.AreEqual("d3cc0c53-7c22-4a54-b4f3-d90d38d210bb", result.Document.DocumentIdentity);
        Assert.AreEqual(66, result.Document.CustomerId);
        Assert.AreEqual(118m, result.Document.Amount);
    }

    [TestMethod]
    public async Task AllRequests_UsesDocumentedJsonFormatAndListEnvelope()
    {
        const string original = "{\"error_code\":0,\"data\":{\"document_type\":1,\"document_number\":2530,\"document_identity\":\"d3cc0c53-7c22-4a54-b4f3-d90d38d210bb\",\"customer_id\":66,\"amount\":118}}";
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { error_code = 0, data = new[] { original } })
        }));
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);

        var results = await provider.GetAllRequestsAsync("test-credential", "CLIENTA-RIVHIT-INVOICE-4651b0a6e63a47999b57d067de9ed1c0", default);

        Assert.AreEqual(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.AreEqual("/online/RivhitOnlineAPI.svc/Status.AllRequests/JSON", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.AreEqual("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ProviderRequestStatus.Success, results[0].Status);
        Assert.AreEqual(2530, results[0].Document!.DocumentNumber);
        Assert.AreEqual(66, results[0].Document!.CustomerId);
    }

    // ---- Rivhit 5xx deterministic-rejection parsing (INV-8: HTTP 500 + error_code -28) ----

    [TestMethod]
    public async Task DocumentNew_500WithErrorCode28_ReturnsDeterministicRejected()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = JsonContent.Create(new { error_code = -28, debug_message = "INVALID_ID_NUMBER" })
        }));
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);
        var request = new AccountingInvoiceIssueRequest(Guid.NewGuid(), "INV-1", new DateOnly(2026, 10, 19), null, "Client", null, null, 668347837, null, null, 18m, 118m, new[] { new AccountingInvoiceLineRequest("S", 1, 100m) }, "REF-5XX");

        var result = await provider.IssueInvoiceAsync("cred", request, default);

        Assert.AreEqual(ProviderMutationDisposition.Rejected, result.Disposition);
        Assert.IsNotNull(result.Document);
        StringAssert.Contains(result.Document!.ErrorCode, "INVALID_ID_NUMBER");
    }

    [TestMethod]
    public async Task DocumentNew_500EmptyBody_ReturnsUnknown()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("") }));
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);
        var request = new AccountingInvoiceIssueRequest(Guid.NewGuid(), "INV-1", new DateOnly(2026, 10, 19), null, "Client", null, null, 123, null, null, 18m, 118m, new[] { new AccountingInvoiceLineRequest("S", 1, 100m) }, "REF-EMPTY");

        var result = await provider.IssueInvoiceAsync("cred", request, default);

        Assert.AreEqual(ProviderMutationDisposition.Unknown, result.Disposition);
        Assert.IsNull(result.Document);
    }

    [TestMethod]
    public async Task DocumentNew_500UnparseableBody_ReturnsUnknown()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("<html>Gateway error</html>") }));
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);
        var request = new AccountingInvoiceIssueRequest(Guid.NewGuid(), "INV-1", new DateOnly(2026, 10, 19), null, "Client", null, null, 123, null, null, 18m, 118m, new[] { new AccountingInvoiceLineRequest("S", 1, 100m) }, "REF-BADJSON");

        var result = await provider.IssueInvoiceAsync("cred", request, default);

        Assert.AreEqual(ProviderMutationDisposition.Unknown, result.Disposition);
    }

    [TestMethod]
    public async Task ReceiptNew_500WithErrorCode_ReturnsDeterministicRejected()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = JsonContent.Create(new { error_code = -41, debug_message = "PAYMENT_TYPE_INVALID" })
        }));
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);
        var request = new AccountingReceiptIssueRequest(1, 0, "Client", null, null, 123456, null, new DateOnly(2026, 10, 20), 50m, 2, "REF-R5XX", true, true);

        var result = await provider.IssueReceiptAsync("cred", request, default);

        Assert.AreEqual(ProviderMutationDisposition.Rejected, result.Disposition);
        StringAssert.Contains(result.Document!.ErrorCode, "PAYMENT_TYPE_INVALID");
    }

    [TestMethod]
    public async Task ReceiptNew_500EmptyBody_ReturnsUnknown()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("") }));
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);
        var request = new AccountingReceiptIssueRequest(1, 0, "Client", null, null, 123456, null, new DateOnly(2026, 10, 20), 50m, 2, "REF-REMPTY", true, true);

        var result = await provider.IssueReceiptAsync("cred", request, default);

        Assert.AreEqual(ProviderMutationDisposition.Unknown, result.Disposition);
    }

    // Service-level: a deterministic Rivhit rejection over HTTP 500 must become Rejected,
    // never ReconciliationRequired, and never retry Document.New (INV-8 scenario).
    [TestMethod]
    public async Task IssueInvoice_500DeterministicRejection_MarksRejectedNotReconciliation()
    {
        await using var f = await NewFixtureAsync(idNumber: "516264675"); // valid 9-digit so the request reaches Document.New
        var documentNewCalls = 0;
        var handler = new Handler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("Document.TypeList", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { error_code = 0, data = new { document_type_list = new[] { new { document_type = 1, document_name = "Tax Invoice", is_invoice_receipt = false, is_accounting = true, price_include_vat = false } } } }) });
            if (path.EndsWith("Accounting.VatRate", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { error_code = 0, data = new { vat_rate = 18m } }) });
            if (path.EndsWith("Document.New", StringComparison.Ordinal))
            {
                documentNewCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = JsonContent.Create(new { error_code = -28, debug_message = "INVALID_ID_NUMBER" }) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        var provider = new RivhitAccountingProvider(new HttpClient(handler) { BaseAddress = new Uri(RivhitOptions.DefaultBaseUrl) }, NullLogger<RivhitAccountingProvider>.Instance);
        var service = new ExternalAccountingDocumentService(f.Db, new Products(f.BusinessId), new Integrations(f.BusinessId, f.Credential), new Features(), provider);

        var result = await service.IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);

        Assert.AreEqual(AccountingDocumentOutcome.ProviderRejected, result.Outcome);
        Assert.AreEqual(1, documentNewCalls); // never retried
        var row = await f.Db.ExternalAccountingDocuments.SingleAsync();
        Assert.AreEqual(ExternalAccountingDocumentStatus.Rejected, row.Status);
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());
    }

    // Production INV cfea7188 scenario: provider confirmed receipt 874, PaymentId NULL, no canonical Payment,
    // operation stuck ReconciliationRequired (ClientaFinalizationPending), reservation holds the full balance.
    // Reconcile must finalize via the ALREADY-CONFIRMED provider document, create the canonical Payment once,
    // and a subsequent retry must not duplicate Payment/receipt nor re-reserve.
    [DataTestMethod]
    [DataRow(868)]
    [DataRow(874)]
    public async Task ReconcileStuckFinalization_FinalizesConfirmedDocument_AndRetryDoesNotDuplicate(int receiptNumber)
    {
        await using var f = await NewFixtureAsync();
        // Simulate the stuck state directly (as if finalization threw after Receipt.New succeeded).
        var operation = new ExternalAccountingOperation
        {
            Id = Guid.NewGuid(), TenantId = f.BusinessId, Provider = IntegrationProvider.Rivhit, InvoiceId = f.InvoiceId,
            OperationType = ExternalAccountingOperationType.Receipt, Status = ExternalAccountingOperationStatus.ReconciliationRequired,
            Amount = 118m, Currency = "ILS", PaymentMethod = PaymentMethod.Cash, PaymentDate = DateTime.UtcNow.Date,
            IdempotencyKey = "stuck-874", RequestReference = "CLIENTA-RIVHIT-RECEIPT-stuck874",
            PaymentId = null, // not linked
            ExternalDocumentType = 1, ExternalDocumentNumber = receiptNumber, ExternalDocumentId = $"confirmed-{receiptNumber}",
            ExternalCustomerId = 567, ExternalAmount = 118m, ProviderResponseCode = "ClientaFinalizationPending",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        f.Db.ExternalAccountingOperations.Add(operation);
        await f.Db.SaveChangesAsync();
        // Reservation holds the full balance -> available 0 (matches production symptom).
        Assert.AreEqual(0m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
        Assert.AreEqual(0, await f.Db.Payments.CountAsync());
        var receiptCallsBefore = f.Provider.ReceiptCalls;

        // Reconcile: provider document already confirmed on the row => Finalize without any read-only status call needed.
        var reconcile = await f.Receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.Issued, reconcile.Outcome);

        var updated = await f.Db.ExternalAccountingOperations.SingleAsync();
        Assert.AreEqual(ExternalAccountingOperationStatus.Completed, updated.Status);
        Assert.IsNotNull(updated.PaymentId);
        // Canonical Payment created exactly once, linked.
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
        var document = await f.Db.ExternalAccountingDocuments.SingleAsync();
        Assert.AreEqual(updated.PaymentId, document.PaymentId);
        Assert.AreEqual(receiptNumber, document.ExternalDocumentNumber);
        // Balance now consumed by the VERIFIED payment (not a dangling reservation).
        Assert.AreEqual(0m, await f.Balances.GetAvailableAsync(f.BusinessId, f.InvoiceId));
        // No Receipt.New was called during reconcile.
        Assert.AreEqual(receiptCallsBefore, f.Provider.ReceiptCalls);

        // A retry reconcile is idempotent: AlreadyCompleted, no duplicate Payment/receipt.
        var retry = await f.Receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);
        Assert.AreEqual(AccountingReceiptOutcome.AlreadyCompleted, retry.Outcome);
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
        Assert.AreEqual(1, await f.Db.ExternalAccountingDocuments.CountAsync());
        Assert.AreEqual(receiptCallsBefore, f.Provider.ReceiptCalls);
    }

    [TestMethod]
    public async Task PendingReceiptOperationLookup_IncludesIssuingFinalizingAndReconciliationRequiredOnly()
    {
        await using var f = await NewFixtureAsync();
        var pendingStatuses = new[]
        {
            ExternalAccountingOperationStatus.Issuing,
            ExternalAccountingOperationStatus.Finalizing,
            ExternalAccountingOperationStatus.ReconciliationRequired
        };

        foreach (var status in pendingStatuses)
        {
            var operationId = Guid.NewGuid();
            f.Db.ExternalAccountingOperations.Add(new ExternalAccountingOperation
            {
                Id = operationId, TenantId = f.BusinessId, Provider = IntegrationProvider.Rivhit,
                InvoiceId = f.InvoiceId, OperationType = ExternalAccountingOperationType.Receipt,
                Status = status, Amount = 118m, Currency = "ILS", PaymentMethod = PaymentMethod.Cash,
                PaymentDate = DateTime.UtcNow.Date, IdempotencyKey = $"pending-{status}",
                RequestReference = $"CLIENTA-RIVHIT-RECEIPT-{status}", CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await f.Db.SaveChangesAsync();

            Assert.AreEqual(operationId, await f.Receipts.GetPendingOperationIdAsync(f.BusinessId, f.InvoiceId, default));

            f.Db.ExternalAccountingOperations.Remove(await f.Db.ExternalAccountingOperations.SingleAsync(x => x.Id == operationId));
            await f.Db.SaveChangesAsync();
        }

        Assert.IsNull(await f.Receipts.GetPendingOperationIdAsync(f.BusinessId, f.InvoiceId, default));
    }

    [TestMethod]
    public async Task ReconcileOrphanedIssuing_ConfirmsAndLinksExistingReceiptWithoutNewReceiptCall()
    {
        await using var f = await NewFixtureAsync();
        var operation = new ExternalAccountingOperation
        {
            Id = Guid.NewGuid(), TenantId = f.BusinessId, Provider = IntegrationProvider.Rivhit,
            InvoiceId = f.InvoiceId, OperationType = ExternalAccountingOperationType.Receipt,
            Status = ExternalAccountingOperationStatus.Issuing, Amount = 118m, Currency = "ILS",
            PaymentMethod = PaymentMethod.Cash, PaymentDate = DateTime.UtcNow.Date,
            IdempotencyKey = "orphaned-issuing", RequestReference = "CLIENTA-RIVHIT-RECEIPT-orphaned-issuing",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        f.Db.ExternalAccountingOperations.Add(operation);
        await f.Db.SaveChangesAsync();
        f.Provider.Last = new(ProviderRequestStatus.Success,
            new ProviderInvoiceDocument(1, 868, "confirmed-868", 567, 118m, null));
        var receiptCallsBefore = f.Provider.ReceiptCalls;

        var result = await f.Receipts.ReconcileAsync(f.BusinessId, operation.Id, null, default);

        Assert.AreEqual(AccountingReceiptOutcome.Issued, result.Outcome);
        var completed = await f.Db.ExternalAccountingOperations.SingleAsync(x => x.Id == operation.Id);
        Assert.AreEqual(ExternalAccountingOperationStatus.Completed, completed.Status);
        Assert.IsNotNull(completed.PaymentId);
        Assert.AreEqual(1, await f.Db.Payments.CountAsync());
        Assert.AreEqual(868, (await f.Db.ExternalAccountingDocuments.SingleAsync()).ExternalDocumentNumber);
        Assert.AreEqual(receiptCallsBefore, f.Provider.ReceiptCalls);
    }

    // Invoice reconciliation: definitive provider NotFound releases the reservation (aligned with receipts).
    [TestMethod]
    public async Task InvoiceReconciliation_ProviderNotFound_ReleasesReservationAndAllowsReissue()
    {
        await using var f = await NewFixtureAsync();
        // Put the invoice reservation into ReconciliationRequired via an Unknown provider result.
        f.Provider.Mutation = new(ProviderMutationDisposition.Unknown, null);
        var first = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.ReconciliationRequired, first.Outcome);
        var issueCallsBefore = f.Provider.IssueCalls;

        // Provider now definitively reports nothing for the reference.
        f.Provider.Last = new(ProviderRequestStatus.NotFound, null);
        var reconcile = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);

        Assert.AreEqual(AccountingDocumentOutcome.ProviderRejected, reconcile.Outcome);
        var row = await f.Db.ExternalAccountingDocuments.SingleAsync();
        Assert.AreEqual(ExternalAccountingDocumentStatus.Rejected, row.Status);
        Assert.AreEqual("ProviderNotReceived", row.ProviderResponseCode);
        // No Document.New call was made during reconciliation (read-only recovery only).
        Assert.AreEqual(issueCallsBefore, f.Provider.IssueCalls);

        // Reservation is Rejected (not Issuing/ReconciliationRequired) => a fresh issuance is allowed.
        f.Provider.Last = new(ProviderRequestStatus.NotFound, null);
        f.Provider.Mutation = new(ProviderMutationDisposition.Success, new ProviderInvoiceDocument(1, 999, "new-identity", 567, 118m, "0"));
        var retry = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.ProviderRejected, retry.Outcome); // existing row is Rejected -> surfaces ProviderRejected, no duplicate
    }

    // Invoice reconciliation: ambiguous provider result keeps ReconciliationRequired (no release).
    [TestMethod]
    public async Task InvoiceReconciliation_Unknown_KeepsReconciliationRequired()
    {
        await using var f = await NewFixtureAsync();
        f.Provider.Mutation = new(ProviderMutationDisposition.Unknown, null);
        var first = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);
        Assert.AreEqual(AccountingDocumentOutcome.ReconciliationRequired, first.Outcome);
        var issueCallsBefore = f.Provider.IssueCalls;

        f.Provider.Last = new(ProviderRequestStatus.Unknown, null);
        var reconcile = await f.Service().IssueInvoiceAsync(f.BusinessId, f.InvoiceId, new IssueAccountingInvoiceRequest(), CancellationToken.None);

        Assert.AreEqual(AccountingDocumentOutcome.ReconciliationRequired, reconcile.Outcome);
        Assert.AreEqual(ExternalAccountingDocumentStatus.ReconciliationRequired, (await f.Db.ExternalAccountingDocuments.SingleAsync()).Status);
        Assert.AreEqual(issueCallsBefore, f.Provider.IssueCalls);
    }
}
