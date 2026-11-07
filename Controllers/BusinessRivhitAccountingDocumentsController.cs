using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Clienta.Api.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/business/invoices/{invoiceId:guid}/accounting-document")]
[Authorize(Policy = "manage_invoices")]
public sealed class BusinessRivhitAccountingDocumentsController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantProductService _products;
    private readonly IExternalAccountingDocumentService _documents;
    private readonly IExternalAccountingReceiptService _receipts;
    private readonly IExternalAccountingArchiveService _archive;
    private readonly AppDbContext _db;

    public BusinessRivhitAccountingDocumentsController(
        ITenantContext tenantContext,
        ITenantProductService products,
        IExternalAccountingDocumentService documents,
        IExternalAccountingReceiptService receipts,
        IExternalAccountingArchiveService archive,
        AppDbContext db)
    {
        _tenantContext = tenantContext;
        _products = products;
        _documents = documents;
        _receipts = receipts;
        _archive = archive;
        _db = db;
    }

    [HttpGet("/api/business/invoices/accounting-documents")]
    [Authorize(Policy = "manage_invoices")]
    public async Task<IActionResult> GetForTenant(CancellationToken ct)
    {
        var gate = await GateAsync(ct);
        if (gate is not null) return gate;
        return Ok(await _documents.GetTenantDocumentsAsync(_tenantContext.TenantId, ct));
    }

    [HttpGet]
    [Authorize(Policy = "manage_invoices")]
    [ProducesResponseType(typeof(ExternalAccountingDocumentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid invoiceId, CancellationToken ct)
    {
        var gate = await GateAsync(ct);
        if (gate is not null) return gate;
        var document = await _documents.GetInvoiceDocumentAsync(_tenantContext.TenantId, invoiceId, ct);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpPost("rivhit")]
    [Authorize(Policy = IntegrationAuthorization.ManagePolicy)]
    [ProducesResponseType(typeof(ExternalAccountingDocumentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Issue(Guid invoiceId,[FromBody] IssueAccountingInvoiceRequest request,CancellationToken ct)
    {
        var gate = await GateAsync(ct);
        if (gate is not null) return gate;

        var result = await _documents.IssueInvoiceAsync(_tenantContext.TenantId,invoiceId,request,ct);
        return result.Outcome switch
        {
            AccountingDocumentOutcome.Issued or AccountingDocumentOutcome.AlreadyIssued => Ok(result.Document),
            AccountingDocumentOutcome.Issuing => Conflict(new { code = "Issuing", document = result.Document }),
            AccountingDocumentOutcome.ReconciliationRequired => Conflict(new { code = "ReconciliationRequired", document = result.Document }),
            AccountingDocumentOutcome.InvoiceNotFound => NotFound(new { code = "InvoiceNotFound" }),
            AccountingDocumentOutcome.UnsupportedTenant => StatusCode(StatusCodes.Status403Forbidden, new { code = "BusinessOnly" }),
            AccountingDocumentOutcome.InvoiceStateUnsupported => Conflict(new { code = "InvoiceStateUnsupported" }),
            AccountingDocumentOutcome.CurrencyUnsupported => BadRequest(new { code = "CurrencyUnsupported" }),
            AccountingDocumentOutcome.CustomerIdentifierRequired => BadRequest(new { code = "CustomerIdentifierRequired" }),
            AccountingDocumentOutcome.CustomerIdentifierInvalid => UnprocessableEntity(new { code = "CustomerIdentifierInvalid" }),
            AccountingDocumentOutcome.VatMismatch => Conflict(new { code = "VatMismatch" }),
            AccountingDocumentOutcome.InvoiceDataInvalid => BadRequest(new { code = "InvoiceDataInvalid" }),
            AccountingDocumentOutcome.IntegrationUnavailable or AccountingDocumentOutcome.CredentialUnavailable => Conflict(new { code = result.Outcome.ToString() }),
            AccountingDocumentOutcome.ProviderRejected => UnprocessableEntity(new { code = "ProviderRejected", document = result.Document }),
            AccountingDocumentOutcome.ProviderResponseMismatch => Conflict(new { code = "ProviderResponseMismatch", document = result.Document }),
            _ => Conflict(new { code = "ReservationConflict", document = result.Document })
        };
    }

    [HttpPost("receipt")]
    [Authorize(Policy = IntegrationAuthorization.ManagePolicy)]
    public async Task<IActionResult> IssueReceipt(Guid invoiceId, [FromBody] IssueAccountingReceiptRequest request, CancellationToken ct)
    {
        var gate = await GateAsync(ct);
        if (gate is not null) return gate;
        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (Guid?)null;
        var result = await _receipts.IssueAsync(_tenantContext.TenantId, invoiceId, request, userId, ct);
        return MapReceiptResult(result);
    }

    [HttpPost("/api/business/accounting-operations/{operationId:guid}/reconcile")]
    [Authorize(Policy = IntegrationAuthorization.ManagePolicy)]
    public async Task<IActionResult> ReconcileReceipt(Guid operationId, CancellationToken ct)
    {
        var gate = await GateAsync(ct);
        if (gate is not null) return gate;
        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (Guid?)null;
        return MapReceiptResult(await _receipts.ReconcileAsync(_tenantContext.TenantId, operationId, userId, ct));
    }

    /// <summary>Read-only: resolves the invoice's pending receipt operation (reconcile target), if any.</summary>
    [HttpGet("receipt-pending-operation")]
    [Authorize(Policy = "manage_invoices")]
    public async Task<IActionResult> GetPendingReceiptOperation(Guid invoiceId, CancellationToken ct)
    {
        var gate = await GateAsync(ct);
        if (gate is not null) return gate;
        var operationId = await _receipts.GetPendingOperationIdAsync(_tenantContext.TenantId, invoiceId, ct);
        return operationId.HasValue ? Ok(new { operationId }) : Ok(new { operationId = (Guid?)null });
    }

    /// <summary>
    /// Safe "check status" for an invoice in Issuing/ReconciliationRequired. Uses the existing read-only
    /// Rivhit recovery (Status.LastRequest / Status.AllRequests) via IssueInvoiceAsync — it never calls
    /// Document.New for an outstanding row. Confirmed => finalize; definitive rejection/NotFound => Rejected
    /// (reservation released); ambiguous => remains ReconciliationRequired.
    /// </summary>
    [HttpPost("reconcile")]
    [Authorize(Policy = IntegrationAuthorization.ManagePolicy)]
    public async Task<IActionResult> ReconcileInvoice(Guid invoiceId, CancellationToken ct)
    {
        var gate = await GateAsync(ct);
        if (gate is not null) return gate;
        if (await _products.GetAsync(_tenantContext.TenantId, ct) is (ResolvedTenantProduct.Clinic or ResolvedTenantProduct.Legacy))
        {
            var existing = await _documents.GetInvoiceDocumentAsync(_tenantContext.TenantId, invoiceId, ct);
            if (existing?.Status is not ("Issuing" or "ReconciliationRequired"))
                return Conflict(new { code = "NoReconciliationRequired" });
        }
        var result = await _documents.IssueInvoiceAsync(_tenantContext.TenantId, invoiceId, new IssueAccountingInvoiceRequest(), ct);
        return result.Outcome switch
        {
            AccountingDocumentOutcome.Issued or AccountingDocumentOutcome.AlreadyIssued => Ok(new { code = "Resolved", document = result.Document }),
            AccountingDocumentOutcome.ProviderRejected => UnprocessableEntity(new { code = "ProviderRejected", document = result.Document }),
            AccountingDocumentOutcome.ReconciliationRequired => Conflict(new { code = "ReconciliationRequired", document = result.Document }),
            AccountingDocumentOutcome.InvoiceNotFound => NotFound(new { code = "InvoiceNotFound" }),
            AccountingDocumentOutcome.UnsupportedTenant => StatusCode(StatusCodes.Status403Forbidden, new { code = "BusinessOnly" }),
            _ => Conflict(new { code = result.Outcome.ToString(), document = result.Document })
        };
    }

    /// <summary>
    /// Safe archive/retry for an already-Issued Rivhit document whose ArchiveStatus is Pending/Failed.
    /// Only performs the read-only Rivhit Copy + PDF download and saves via ExternalAccountingArchiveService
    /// (S3 + ClientFile). Never calls Document.New/Receipt.New and never creates financial records.
    /// </summary>
    [HttpPost("/api/business/accounting-documents/{documentId:guid}/archive")]
    [Authorize(Policy = IntegrationAuthorization.ManagePolicy)]
    public async Task<IActionResult> ArchiveDocument(Guid documentId, CancellationToken ct)
    {
        var gate = await GateAsync(ct);
        if (gate is not null) return gate;
        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
        var result = await _archive.ArchiveAsync(_tenantContext.TenantId, documentId, userId, ct);
        return result.Outcome switch
        {
            AccountingArchiveOutcome.Archived => Ok(new { code = "Archived", clientFileId = result.ClientFileId }),
            AccountingArchiveOutcome.AlreadyArchived => Ok(new { code = "AlreadyArchived", clientFileId = result.ClientFileId }),
            AccountingArchiveOutcome.DocumentNotFound => NotFound(new { code = "DocumentNotFound" }),
            AccountingArchiveOutcome.DocumentNotIssued => Conflict(new { code = "DocumentNotIssued" }),
            AccountingArchiveOutcome.CredentialUnavailable => Conflict(new { code = "IntegrationUnavailable" }),
            AccountingArchiveOutcome.CopyUnavailable => Conflict(new { code = "CopyUnavailable", result.ErrorCode }),
            AccountingArchiveOutcome.DownloadRejected => Conflict(new { code = "DownloadRejected", result.ErrorCode }),
            _ => Conflict(new { code = "StorageFailed", result.ErrorCode })
        };
    }

    private static IActionResult MapReceiptResult(AccountingReceiptResult result) => result.Outcome switch
    {
        AccountingReceiptOutcome.Issued or AccountingReceiptOutcome.AlreadyCompleted => new OkObjectResult(new { result.OperationId, result.Document }),
        AccountingReceiptOutcome.UnsupportedTenant => new ObjectResult(new { code = "BusinessOnly" }) { StatusCode = StatusCodes.Status403Forbidden },
        AccountingReceiptOutcome.ExistingPaymentRequired => new ObjectResult(new { code = "ExistingPaymentRequired" }) { StatusCode = StatusCodes.Status403Forbidden },
        AccountingReceiptOutcome.InvoiceNotFound => new NotFoundObjectResult(new { code = "InvoiceNotFound" }),
        AccountingReceiptOutcome.UnsupportedPaymentMethod => new BadRequestObjectResult(new { code = "UnsupportedPaymentMethod" }),
        AccountingReceiptOutcome.InvalidAmount => new BadRequestObjectResult(new { code = "InvalidAmount" }),
        AccountingReceiptOutcome.InvalidPaymentMethod => new BadRequestObjectResult(new { code = "InvalidPaymentMethod" }),
        AccountingReceiptOutcome.InvoiceIneligible => new ConflictObjectResult(new { code = "InvoiceIneligible" }),
        AccountingReceiptOutcome.AmountUnavailable => new ConflictObjectResult(new { code = "AmountUnavailable", result.AvailableBalance }),
        AccountingReceiptOutcome.CustomerIdentifierInvalid => new UnprocessableEntityObjectResult(new { code = "CustomerIdentifierInvalid" }),
        AccountingReceiptOutcome.PaymentNotFound => new NotFoundObjectResult(new { code = "PaymentNotFound" }),
        AccountingReceiptOutcome.IdempotencyConflict => new ConflictObjectResult(new { code = "IdempotencyConflict", result.OperationId }),
        AccountingReceiptOutcome.ProviderRejected => new UnprocessableEntityObjectResult(new { code = "ProviderRejected", result.OperationId }),
        AccountingReceiptOutcome.IntegrationUnavailable => new ConflictObjectResult(new { code = "IntegrationUnavailable" }),
        _ => new ConflictObjectResult(new { code = "ReconciliationRequired", result.OperationId })
    };

    private async Task<IActionResult?> GateAsync(CancellationToken ct)
    {
        if (_tenantContext.TenantId == Guid.Empty) return Unauthorized();
        if (!await _db.Tenants.IgnoreQueryFilters().AnyAsync(tenant => tenant.Id == _tenantContext.TenantId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "BusinessOnly" });
        var product = await _products.GetAsync(_tenantContext.TenantId, ct);
        if (product is not (ResolvedTenantProduct.Business or ResolvedTenantProduct.Clinic or ResolvedTenantProduct.Legacy))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "BusinessOnly" });
        return null;
    }
}
