using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Clienta.Api.Services.Integrations;

public enum AccountingReceiptOutcome
{
    Issued, AlreadyCompleted, ReconciliationRequired, InvoiceNotFound, UnsupportedTenant, IntegrationUnavailable,
    UnsupportedPaymentMethod, InvoiceIneligible, InvalidAmount, AmountUnavailable, IdempotencyConflict, ProviderRejected,
    CustomerIdentifierInvalid, PaymentNotFound, InvalidPaymentMethod
}

public sealed record AccountingReceiptResult(AccountingReceiptOutcome Outcome, Guid? OperationId = null,
    ExternalAccountingDocumentResponse? Document = null, decimal? AvailableBalance = null);

public interface IExternalAccountingReceiptService
{
    Task<AccountingReceiptResult> IssueAsync(Guid tenantId, Guid invoiceId, IssueAccountingReceiptRequest request, Guid? userId, CancellationToken ct);
    Task<AccountingReceiptResult> ReconcileAsync(Guid tenantId, Guid operationId, Guid? userId, CancellationToken ct);
    /// <summary>Read-only: returns the invoice's latest unresolved receipt operation id (Issuing/Finalizing/ReconciliationRequired), if any.</summary>
    Task<Guid?> GetPendingOperationIdAsync(Guid tenantId, Guid invoiceId, CancellationToken ct);
}

public sealed class ExternalAccountingReceiptService(
    AppDbContext db,
    ITenantContext tenantContext,
    ITenantProductService products,
    ITenantIntegrationService integrations,
    IAccountingDocumentProvider provider,
    IInvoicePaymentService payments,
    IInvoiceBalanceService balances,
    IExternalAccountingArchiveService? archive = null,
    ILogger<ExternalAccountingReceiptService>? logger = null) : IExternalAccountingReceiptService
{
    private const IntegrationProvider ProviderKind = IntegrationProvider.Rivhit;
    private const int NormalReceiptType = 1;
    private const decimal Tolerance = 0.01m;

    public async Task<AccountingReceiptResult> IssueAsync(Guid tenantId, Guid invoiceId, IssueAccountingReceiptRequest request, Guid? userId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || tenantId != tenantContext.TenantId
            || await products.GetAsync(tenantId, ct) != ResolvedTenantProduct.Business)
            return new(AccountingReceiptOutcome.UnsupportedTenant);
        var method = request.PaymentId.HasValue ? null : ParseV1Method(request.PaymentMethod);
        if (!request.PaymentId.HasValue && method is null) return new(AccountingReceiptOutcome.UnsupportedPaymentMethod);
        if (request.Amount <= 0m) return new(AccountingReceiptOutcome.InvalidAmount);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 100)
            return new(AccountingReceiptOutcome.IdempotencyConflict);
        if (request.DeliveryChannel is not (AccountingDeliveryChannel.None or AccountingDeliveryChannel.Email))
            return new(AccountingReceiptOutcome.InvoiceIneligible);

        var credential = await integrations.GetOperationalCredentialAsync(tenantId, ProviderKind, ct);
        if (string.IsNullOrWhiteSpace(credential)) return new(AccountingReceiptOutcome.IntegrationUnavailable);
        var requestReference = BuildRequestReference(tenantId, invoiceId, request.IdempotencyKey.Trim());
        if (request.PaymentId.HasValue)
            return await IssueForExistingPaymentAsync(tenantId, invoiceId, request, credential, requestReference, userId, ct);

        var reservation = await ReserveAsync(tenantId, invoiceId, request, method!.Value, requestReference, ct);
        if (reservation.Outcome != AccountingReceiptOutcome.Issued || reservation.OperationId is null) return reservation;
        var operation = await db.ExternalAccountingOperations.FirstAsync(item => item.Id == reservation.OperationId.Value, ct);
        if (operation.Status != ExternalAccountingOperationStatus.Issuing)
            return await ExistingResultAsync(operation, ct);
        return await IssueReservedReceiptAsync(operation, credential, userId, request.Amount, ct);
    }

    public async Task<Guid?> GetPendingOperationIdAsync(Guid tenantId, Guid invoiceId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) return null;
        return await db.ExternalAccountingOperations.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.InvoiceId == invoiceId && item.Provider == ProviderKind
                && (item.Status == ExternalAccountingOperationStatus.Issuing
                    || item.Status == ExternalAccountingOperationStatus.Finalizing
                    || item.Status == ExternalAccountingOperationStatus.ReconciliationRequired))
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AccountingReceiptResult> ReconcileAsync(Guid tenantId, Guid operationId, Guid? userId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || tenantId != tenantContext.TenantId
            || await products.GetAsync(tenantId, ct) != ResolvedTenantProduct.Business)
            return new(AccountingReceiptOutcome.UnsupportedTenant);
        var operation = await db.ExternalAccountingOperations.FirstOrDefaultAsync(item =>
            item.Id == operationId && item.TenantId == tenantId && item.Provider == ProviderKind, ct);
        if (operation is null) return new(AccountingReceiptOutcome.InvoiceNotFound);
        if (operation.Status == ExternalAccountingOperationStatus.Completed) return await ExistingResultAsync(operation, ct);
        if (operation.Status == ExternalAccountingOperationStatus.Finalizing)
        {
            if (!HasConfirmedProviderDocument(operation))
                return new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
            return await FinalizeAsync(operation, userId, ct);
        }
        if (operation.Status == ExternalAccountingOperationStatus.ReconciliationRequired)
            return await ReconcileReconciliationRequiredAsync(operation, userId, ct);
        if (operation.Status == ExternalAccountingOperationStatus.Issuing)
            return await ReconcileIssuingAsync(operation, userId, ct);
        return new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
    }

    private async Task<AccountingReceiptResult> ReconcileIssuingAsync(ExternalAccountingOperation operation, Guid? userId, CancellationToken ct)
    {
        var credential = await integrations.GetOperationalCredentialAsync(operation.TenantId, ProviderKind, ct);
        if (string.IsNullOrWhiteSpace(credential)) return new(AccountingReceiptOutcome.IntegrationUnavailable, operation.Id);
        var recovered = await RecoverAsync(credential, operation.RequestReference, ct);
        if (recovered.Status == ProviderRequestStatus.Success && recovered.Document is not null)
        {
            var document = new ProviderReceiptDocument(recovered.Document.DocumentType, recovered.Document.DocumentNumber,
                recovered.Document.CustomerId, recovered.Document.DocumentIdentity, recovered.Document.Amount, null, recovered.Document.ErrorCode);
            if (!IsComplete(document, operation.Amount)) return new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
            ApplyProviderDocument(operation, document);
            operation.Status = ExternalAccountingOperationStatus.Finalizing;
            operation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            return await FinalizeAsync(operation, userId, ct);
        }
        operation.Status = ExternalAccountingOperationStatus.ReconciliationRequired;
        operation.ProviderResponseCode = "ReconciliationRequired";
        operation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        return new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
    }

    /// <summary>
    /// Reconciles a ReconciliationRequired receipt operation using ONLY read-only Rivhit status APIs
    /// (Status.LastRequest / Status.AllRequests). Never calls Receipt.New, so no duplicate official
    /// document can be created. Outcomes:
    ///  - provider confirms the document  -> finalize/link it;
    ///  - provider definitively reports no document -> release the reservation (mark Rejected);
    ///  - still ambiguous                  -> keep ReconciliationRequired.
    /// </summary>
    private async Task<AccountingReceiptResult> ReconcileReconciliationRequiredAsync(
        ExternalAccountingOperation operation, Guid? userId, CancellationToken ct)
    {
        var credential = await integrations.GetOperationalCredentialAsync(operation.TenantId, ProviderKind, ct);
        if (string.IsNullOrWhiteSpace(credential)) return new(AccountingReceiptOutcome.IntegrationUnavailable, operation.Id);

        // Fast path: we already captured a confirmed provider document before it was flagged.
        if (HasConfirmedProviderDocument(operation))
            return await FinalizeAsync(operation, userId, ct);

        var recovered = await RecoverAsync(credential, operation.RequestReference, ct);

        if (recovered.Status == ProviderRequestStatus.Success && recovered.Document is not null)
        {
            var document = new ProviderReceiptDocument(
                recovered.Document.DocumentType,
                recovered.Document.DocumentNumber,
                recovered.Document.CustomerId,
                recovered.Document.DocumentIdentity,
                recovered.Document.Amount,
                null,
                recovered.Document.ErrorCode);
            if (!IsComplete(document, operation.Amount))
            {
                // Provider returned something but it does not match this operation: stay ambiguous.
                return new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
            }
            ApplyProviderDocument(operation, document);
            operation.Status = ExternalAccountingOperationStatus.Finalizing;
            operation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            return await FinalizeAsync(operation, userId, ct);
        }

        if (recovered.Status == ProviderRequestStatus.Rejected)
        {
            operation.Status = ExternalAccountingOperationStatus.Rejected;
            operation.ProviderResponseCode = SanitizeCode(recovered.Document?.ErrorCode) ?? "ProviderRejected";
            operation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            return new(AccountingReceiptOutcome.ProviderRejected, operation.Id);
        }

        if (recovered.Status == ProviderRequestStatus.NotFound)
        {
            // Provider definitively has no document for this request reference: release the reservation.
            operation.Status = ExternalAccountingOperationStatus.Rejected;
            operation.ProviderResponseCode = "ProviderNotReceived";
            operation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            return new(AccountingReceiptOutcome.ProviderRejected, operation.Id);
        }

        // Unknown: keep the safe blocking state; no balance is released and no duplicate is issued.
        return new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
    }

    /// <summary>Read-only recovery: LastRequest first, then AllRequests (mirrors invoice recovery).</summary>
    private async Task<ProviderRequestRecovery> RecoverAsync(string credential, string requestReference, CancellationToken ct)
    {
        var last = await provider.GetLastRequestAsync(credential, requestReference, ct);
        if (last.Status is ProviderRequestStatus.Success or ProviderRequestStatus.Rejected)
            return last;

        var all = await provider.GetAllRequestsAsync(credential, requestReference, ct);
        var matching = all.LastOrDefault(x => x.Status is ProviderRequestStatus.Success or ProviderRequestStatus.Rejected);
        return matching ?? (all.Any(x => x.Status == ProviderRequestStatus.Unknown)
            ? new ProviderRequestRecovery(ProviderRequestStatus.Unknown, null)
            : last);
    }

    /// <summary>
    /// Issues a Rivhit receipt for an existing verified CLIENTA Payment. Never reserves invoice
    /// balance and never creates another Payment: the operation is linked to the existing Payment
    /// up-front (PaymentId set => excluded from the reservation bucket in InvoiceBalanceService),
    /// and FinalizeAsync reuses that Payment.
    /// </summary>
    private async Task<AccountingReceiptResult> IssueForExistingPaymentAsync(
        Guid tenantId, Guid invoiceId, IssueAccountingReceiptRequest request,
        string credential, string requestReference, Guid? userId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        AccountingReceiptResult? result = null;
        ExternalAccountingOperation? reservedOperation = null;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (!await LockInvoiceAsync(tenantId, invoiceId, ct))
            {
                result = new(AccountingReceiptOutcome.InvoiceNotFound);
                return;
            }

            var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(item => item.Id == invoiceId && item.TenantId == tenantId, ct);
            if (invoice is null) { result = new(AccountingReceiptOutcome.InvoiceNotFound); return; }
            if (invoice.Status == InvoiceStatus.Cancelled) { result = new(AccountingReceiptOutcome.InvoiceIneligible); return; }
            if (!string.Equals(await db.Tenants.Where(item => item.Id == tenantId).Select(item => item.Currency).FirstOrDefaultAsync(ct), "ILS", StringComparison.OrdinalIgnoreCase))
            { result = new(AccountingReceiptOutcome.InvoiceIneligible); return; }

            var payment = await db.Payments.IgnoreQueryFilters().FirstOrDefaultAsync(item =>
                item.Id == request.PaymentId!.Value && item.TenantId == tenantId && item.InvoiceId == invoiceId, ct);
            if (payment is null || payment.Status != PaymentStatus.Verified)
            { result = new(AccountingReceiptOutcome.PaymentNotFound); return; }

            var roundedAmount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
            if (payment.Amount != roundedAmount)
            { result = new(AccountingReceiptOutcome.InvalidAmount); return; }
            // Idempotency: a completed receipt document already exists for this payment.
            var existingDocument = await db.ExternalAccountingDocuments.IgnoreQueryFilters().FirstOrDefaultAsync(item =>
                item.TenantId == tenantId && item.Provider == ProviderKind && item.PaymentId == payment.Id
                && item.DocumentType == ExternalAccountingPurpose.Receipt, ct);
            if (existingDocument is not null)
            {
                var existingOperation = await db.ExternalAccountingOperations.FirstOrDefaultAsync(item =>
                    item.TenantId == tenantId && item.Provider == ProviderKind && item.PaymentId == payment.Id
                    && item.Status == ExternalAccountingOperationStatus.Completed, ct);
                result = new(AccountingReceiptOutcome.AlreadyCompleted, existingOperation?.Id, Map(existingDocument));
                await transaction.CommitAsync(ct);
                return;
            }

            // Idempotency: same key replay returns the original operation.
            var existing = await db.ExternalAccountingOperations.FirstOrDefaultAsync(item =>
                item.TenantId == tenantId && item.Provider == ProviderKind && item.IdempotencyKey == request.IdempotencyKey.Trim(), ct);
            if (existing is not null)
            {
                result = existing.InvoiceId != invoiceId || existing.PaymentId != payment.Id || existing.Amount != roundedAmount
                    || existing.PaymentMethod != payment.PaymentMethod || existing.DeliveryChannel != request.DeliveryChannel
                    ? new(AccountingReceiptOutcome.IdempotencyConflict, existing.Id)
                    : await ExistingResultAsync(existing, ct);
                await transaction.CommitAsync(ct);
                return;
            }

            var operation = new ExternalAccountingOperation
            {
                TenantId = tenantId,
                Provider = ProviderKind,
                InvoiceId = invoiceId,
                OperationType = ExternalAccountingOperationType.Receipt,
                Status = ExternalAccountingOperationStatus.Issuing,
                Amount = roundedAmount,
                Currency = "ILS",
                PaymentMethod = payment.PaymentMethod,
                PaymentDate = DateTime.SpecifyKind(payment.PaymentDate.Date, DateTimeKind.Utc),
                DeliveryChannel = request.DeliveryChannel,
                IdempotencyKey = request.IdempotencyKey.Trim(),
                RequestReference = requestReference,
                PaymentId = payment.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.ExternalAccountingOperations.Add(operation);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync(ct);
                db.Entry(operation).State = EntityState.Detached;
                var winner = await db.ExternalAccountingOperations.FirstOrDefaultAsync(item =>
                    item.TenantId == tenantId && item.Provider == ProviderKind && item.IdempotencyKey == operation.IdempotencyKey, ct);
                result = winner is null ? new(AccountingReceiptOutcome.IdempotencyConflict)
                    : await ExistingResultAsync(winner, ct);
                return;
            }
            await transaction.CommitAsync(ct);
            reservedOperation = operation;
        });

        if (reservedOperation is null)
            return result ?? new(AccountingReceiptOutcome.IdempotencyConflict);

        return await IssueReservedReceiptAsync(reservedOperation, credential, userId, request.Amount, ct);
    }

    /// <summary>Shared provider-issue + finalize tail for a newly reserved receipt operation.</summary>
    private async Task<AccountingReceiptResult> IssueReservedReceiptAsync(
        ExternalAccountingOperation operation, string credential, Guid? userId, decimal expectedAmount, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().FirstAsync(item => item.Id == operation.InvoiceId && item.TenantId == operation.TenantId, ct);
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(item => item.Id == invoice.ClientId && item.TenantId == operation.TenantId, ct);
        if (client is null)
        {
            await RejectAsync(operation, "CustomerUnavailable");
            return new(AccountingReceiptOutcome.InvoiceIneligible, operation.Id);
        }

        string? deliveryEmail = null;
        if (operation.DeliveryChannel == AccountingDeliveryChannel.Email)
        {
            deliveryEmail = client.Email?.Trim();
            if (string.IsNullOrWhiteSpace(deliveryEmail))
            {
                await RejectAsync(operation, "CustomerEmailRequired");
                return new(AccountingReceiptOutcome.InvoiceIneligible, operation.Id);
            }
        }

        var existingCustomerId = await FindTrustedCustomerIdAsync(operation.TenantId, operation.InvoiceId, ct);
        var safeId = TryGetSafeClientIdNumber(client.IdNumber, out var idIssue);
        if (!existingCustomerId.HasValue && !safeId.HasValue)
        {
            await RejectAsync(operation, "CustomerIdentifierRequired");
            return new(idIssue is not null ? AccountingReceiptOutcome.CustomerIdentifierInvalid : AccountingReceiptOutcome.InvoiceIneligible, operation.Id);
        }

        var type = await ResolveRivhitPaymentTypeAsync(operation.PaymentMethod, credential, ct);
        if (type <= 0)
        {
            await RejectAsync(operation, "UnsupportedPaymentMethod");
            return new(AccountingReceiptOutcome.UnsupportedPaymentMethod, operation.Id);
        }

        var issue = new AccountingReceiptIssueRequest(
            NormalReceiptType,
            existingCustomerId ?? 0,
            client.FullName,
            client.Address,
            client.Phone,
            existingCustomerId.HasValue ? null : safeId,
            existingCustomerId.HasValue ? null : idIssue,
            DateOnly.FromDateTime(operation.PaymentDate),
            operation.Amount,
            type,
            operation.RequestReference,
            CreateCustomer: !existingCustomerId.HasValue,
            FindById: !existingCustomerId.HasValue && safeId.HasValue,
            SendEmail: operation.DeliveryChannel == AccountingDeliveryChannel.Email,
            EmailTo: deliveryEmail);

        ProviderReceiptMutationResult providerResult;
        try
        {
            // Exactly one mutation attempt per reserved operation. Unknown never triggers automatic retry.
            providerResult = await provider.IssueReceiptAsync(credential, issue, ct);
        }
        catch (OperationCanceledException)
        {
            providerResult = new(ProviderMutationDisposition.Unknown, null);
        }
        catch
        {
            providerResult = new(ProviderMutationDisposition.Unknown, null);
        }

        if (providerResult.Disposition == ProviderMutationDisposition.Rejected)
        {
            await RejectAsync(operation, SanitizeCode(providerResult.Document?.ErrorCode) ?? "ProviderRejected");
            return new(AccountingReceiptOutcome.ProviderRejected, operation.Id);
        }
        if (providerResult.Disposition != ProviderMutationDisposition.Success || !IsComplete(providerResult.Document, expectedAmount))
        {
            operation.Status = ExternalAccountingOperationStatus.ReconciliationRequired;
            operation.ProviderResponseCode = "ProviderResultUncertain";
            operation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            return new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
        }

        ApplyProviderDocument(operation, providerResult.Document!);
        operation.Status = ExternalAccountingOperationStatus.Finalizing;
        operation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        return await FinalizeAsync(operation, userId, ct);
    }

    private async Task<AccountingReceiptResult> ReserveAsync(Guid tenantId, Guid invoiceId, IssueAccountingReceiptRequest request,
        PaymentMethod method, string requestReference, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        AccountingReceiptResult? result = null;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (!await LockInvoiceAsync(tenantId, invoiceId, ct))
            {
                result = new(AccountingReceiptOutcome.InvoiceNotFound);
                return;
            }

            var existing = await db.ExternalAccountingOperations.FirstOrDefaultAsync(item =>
                item.TenantId == tenantId && item.Provider == ProviderKind && item.IdempotencyKey == request.IdempotencyKey.Trim(), ct);
            if (existing is not null)
            {
                result = existing.InvoiceId != invoiceId || existing.Amount != Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero)
                    || existing.PaymentMethod != method || existing.PaymentDate.Date != request.PaymentDate.Date || existing.DeliveryChannel != request.DeliveryChannel
                    ? new(AccountingReceiptOutcome.IdempotencyConflict, existing.Id)
                    : await ExistingResultAsync(existing, ct);
                await transaction.CommitAsync(ct);
                return;
            }

            var invoice = await db.Invoices.FirstOrDefaultAsync(item => item.Id == invoiceId && item.TenantId == tenantId, ct);
            if (invoice is null) { result = new(AccountingReceiptOutcome.InvoiceNotFound); return; }
            if (invoice.Status == InvoiceStatus.Cancelled) { result = new(AccountingReceiptOutcome.InvoiceIneligible); return; }
            if (!string.Equals(await db.Tenants.Where(item => item.Id == tenantId).Select(item => item.Currency).FirstOrDefaultAsync(ct), "ILS", StringComparison.OrdinalIgnoreCase))
            { result = new(AccountingReceiptOutcome.InvoiceIneligible); return; }

            var available = await balances.GetAvailableAsync(tenantId, invoiceId, cancellationToken: ct);
            if (request.Amount > available) { result = new(AccountingReceiptOutcome.AmountUnavailable, AvailableBalance: available); return; }

            var operation = new ExternalAccountingOperation
            {
                TenantId = tenantId,
                Provider = ProviderKind,
                InvoiceId = invoiceId,
                OperationType = ExternalAccountingOperationType.Receipt,
                Status = ExternalAccountingOperationStatus.Issuing,
                Amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
                Currency = "ILS",
                PaymentMethod = method,
                PaymentDate = DateTime.SpecifyKind(request.PaymentDate.Date, DateTimeKind.Utc),
                DeliveryChannel = request.DeliveryChannel,
                IdempotencyKey = request.IdempotencyKey.Trim(),
                RequestReference = requestReference,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.ExternalAccountingOperations.Add(operation);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync(ct);
                db.Entry(operation).State = EntityState.Detached;
                var winner = await db.ExternalAccountingOperations.FirstOrDefaultAsync(item =>
                    item.TenantId == tenantId && item.Provider == ProviderKind && item.IdempotencyKey == operation.IdempotencyKey, ct);
                result = winner is null ? new(AccountingReceiptOutcome.IdempotencyConflict)
                    : await ExistingResultAsync(winner, ct);
                return;
            }
            await transaction.CommitAsync(ct);
            result = new(AccountingReceiptOutcome.Issued, operation.Id);
        });
        return result ?? new(AccountingReceiptOutcome.IdempotencyConflict);
    }

    private async Task<AccountingReceiptResult> FinalizeAsync(ExternalAccountingOperation operation, Guid? userId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var finalizeStep = "BeginTransaction";
        string? lastExceptionStep = null;
        ExternalAccountingDocument? documentToArchive = null;
        AccountingReceiptResult? result = null;

        void Trace(string step, Guid operationId, string? details = null)
        {
            finalizeStep = step;
        }

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                // The execution strategy may invoke this delegate again after a transient failure.
                // Discard state from the prior attempt and make every decision from persisted rows.
                db.ChangeTracker.Clear();
                documentToArchive = null;
                lastExceptionStep = null;
                finalizeStep = "BeginTransaction";

                Trace("BeginTransaction", operation.Id,
                    $"TenantId={operation.TenantId} InvoiceId={operation.InvoiceId} Amount={operation.Amount} PaymentId={(operation.PaymentId.HasValue ? operation.PaymentId.Value.ToString() : "NULL")} ExternalDocumentNumber={operation.ExternalDocumentNumber}");
                await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
                try
                {
                    Trace("TransactionStarted", operation.Id);
                    Trace("LockInvoiceAsync", operation.Id, $"InvoiceId={operation.InvoiceId}");
                    if (!await LockInvoiceAsync(operation.TenantId, operation.InvoiceId, ct))
                        throw new InvalidOperationException("Receipt invoice could not be locked.");
                    Trace("InvoiceLocked", operation.Id);

                    Trace("LoadOperation", operation.Id, $"OperationId={operation.Id}");
                    var currentOperation = await db.ExternalAccountingOperations.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(item => item.Id == operation.Id, ct)
                        ?? throw new InvalidOperationException("Receipt operation is no longer available.");

                    if (currentOperation.Status == ExternalAccountingOperationStatus.Completed)
                    {
                        var completedResult = await ExistingResultAsync(currentOperation, ct);
                        documentToArchive = currentOperation.PaymentId.HasValue
                            ? await db.ExternalAccountingDocuments.IgnoreQueryFilters().FirstOrDefaultAsync(item =>
                                item.TenantId == currentOperation.TenantId && item.PaymentId == currentOperation.PaymentId
                                && item.DocumentType == ExternalAccountingPurpose.Receipt
                                && item.ArchiveStatus == AccountingArchiveStatus.Pending, ct)
                            : null;
                        await transaction.CommitAsync(CancellationToken.None);
                        Trace("AlreadyCompleted", currentOperation.Id);
                        result = completedResult;
                        return;
                    }

                    Trace("LoadInvoice", currentOperation.Id, $"InvoiceId={currentOperation.InvoiceId}");
                    var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(item =>
                        item.Id == currentOperation.InvoiceId && item.TenantId == currentOperation.TenantId, ct)
                        ?? throw new InvalidOperationException("Receipt invoice is no longer available.");
                    Trace("InvoiceLoaded", currentOperation.Id, $"Status={invoice.Status} FinalAmountToPay={invoice.FinalAmountToPay}");

                    Trace("LoadLinkedPayment", currentOperation.Id,
                        $"PaymentId={(currentOperation.PaymentId.HasValue ? currentOperation.PaymentId.Value.ToString() : "NULL")}");
                    var payment = currentOperation.PaymentId.HasValue
                        ? await db.Payments.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == currentOperation.PaymentId.Value
                            && item.TenantId == currentOperation.TenantId && item.InvoiceId == currentOperation.InvoiceId, ct)
                        : null;
                    Trace("LinkedPaymentLoaded", currentOperation.Id, $"PaymentId={(payment?.Id.ToString() ?? "NULL")}");

                    Trace("LoadPaymentByRequestReference", currentOperation.Id, $"RequestReference={currentOperation.RequestReference}");
                    payment ??= await db.Payments.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.TenantId == currentOperation.TenantId
                        && item.InvoiceId == currentOperation.InvoiceId && item.Reference == currentOperation.RequestReference
                        && item.Status == PaymentStatus.Verified, ct);
                    Trace("PaymentByRequestReferenceLoaded", currentOperation.Id, $"PaymentId={(payment?.Id.ToString() ?? "NULL")}");

                    if (payment is null)
                    {
                        Trace("CreateClientaPayment", currentOperation.Id,
                            $"Amount={currentOperation.Amount} PaymentDate={currentOperation.PaymentDate:O} PaymentMethod={currentOperation.PaymentMethod} RequestReference={currentOperation.RequestReference}");
                        var paymentResult = await payments.CreateAsync(currentOperation.InvoiceId,
                            new CreatePaymentRequest(currentOperation.Amount, currentOperation.PaymentDate, currentOperation.PaymentMethod.ToString(),
                                currentOperation.RequestReference, "Rivhit receipt"), userId, currentOperation.Id, transactionAlreadyActive: true);
                        Trace("CreateClientaPaymentReturned", currentOperation.Id,
                            $"Outcome={paymentResult.Outcome} Summary={(paymentResult.Summary is null ? "NULL" : "OK")}");
                        if (paymentResult.Outcome != CreateInvoicePaymentOutcome.Created || paymentResult.Summary is null)
                        {
                            currentOperation.Status = ExternalAccountingOperationStatus.ReconciliationRequired;
                            currentOperation.ProviderResponseCode = "ClientaPaymentPending";
                            currentOperation.UpdatedAt = DateTime.UtcNow;
                            Trace("SavePaymentPendingState", currentOperation.Id);
                            await db.SaveChangesAsync(CancellationToken.None);
                            await transaction.CommitAsync(CancellationToken.None);
                            Trace("PaymentPendingStateSaved", currentOperation.Id);
                            result = new(AccountingReceiptOutcome.ReconciliationRequired, currentOperation.Id);
                            return;
                        }

                        Trace("ReloadCreatedPayment", currentOperation.Id, $"RequestReference={currentOperation.RequestReference}");
                        payment = await db.Payments.IgnoreQueryFilters().FirstAsync(item => item.TenantId == currentOperation.TenantId
                            && item.InvoiceId == currentOperation.InvoiceId && item.Reference == currentOperation.RequestReference, ct);
                        Trace("CreatedPaymentReloaded", currentOperation.Id,
                            $"PaymentId={payment.Id} Status={payment.Status} Amount={payment.Amount}");
                        currentOperation.PaymentId = payment.Id;
                    }

                    if (string.Equals(currentOperation.ProviderResponseCode, "ClientaFinalizationPending", StringComparison.Ordinal))
                        currentOperation.ProviderResponseCode = null;

                    Trace("LoadReceiptDocument", currentOperation.Id, $"PaymentId={payment.Id}");
                    var document = await db.ExternalAccountingDocuments.IgnoreQueryFilters().FirstOrDefaultAsync(item =>
                        item.TenantId == currentOperation.TenantId && item.Provider == ProviderKind
                        && item.InvoiceId == currentOperation.InvoiceId && item.PaymentId == payment.Id
                        && item.DocumentType == ExternalAccountingPurpose.Receipt, ct);
                    Trace("ReceiptDocumentLoaded", currentOperation.Id, $"DocumentId={(document?.Id.ToString() ?? "NULL")}");

                    if (document is null)
                    {
                        Trace("CreateReceiptDocumentEntity", currentOperation.Id,
                            $"PaymentId={payment.Id} ExternalDocumentNumber={currentOperation.ExternalDocumentNumber}");
                        document = new ExternalAccountingDocument
                        {
                            TenantId = currentOperation.TenantId,
                            Provider = ProviderKind,
                            InvoiceId = currentOperation.InvoiceId,
                            PaymentId = payment.Id,
                            DocumentType = ExternalAccountingPurpose.Receipt,
                            ExternalDocumentType = currentOperation.ExternalDocumentType,
                            Status = ExternalAccountingDocumentStatus.Issued,
                            RequestReference = currentOperation.RequestReference,
                            ExternalDocumentId = currentOperation.ExternalDocumentId,
                            ExternalDocumentNumber = currentOperation.ExternalDocumentNumber,
                            ExternalCustomerId = currentOperation.ExternalCustomerId,
                            ExternalAmount = currentOperation.ExternalAmount,
                            IssuedAt = DateTime.UtcNow,
                            Currency = "ILS",
                            Amount = currentOperation.Amount,
                            ProviderResponseCode = currentOperation.ProviderResponseCode,
                            DeliveryChannel = currentOperation.DeliveryChannel,
                            DeliveryStatus = currentOperation.DeliveryChannel == AccountingDeliveryChannel.Email
                                ? AccountingDeliveryStatus.Requested
                                : AccountingDeliveryStatus.NotRequested,
                            DeliveryAttemptedAt = currentOperation.DeliveryChannel == AccountingDeliveryChannel.Email
                                ? DateTime.UtcNow
                                : null,
                            DeliveryErrorCode = null,
                            ArchiveStatus = AccountingArchiveStatus.Pending,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        db.ExternalAccountingDocuments.Add(document);
                        Trace("ReceiptDocumentEntityAdded", currentOperation.Id, $"PaymentId={payment.Id}");
                    }

                    currentOperation.PaymentId = payment.Id;
                    currentOperation.Status = ExternalAccountingOperationStatus.Completed;
                    currentOperation.UpdatedAt = DateTime.UtcNow;
                    Trace("SaveFinalizeChanges", currentOperation.Id,
                        $"PaymentId={payment.Id} OperationStatus={currentOperation.Status}");
                    await db.SaveChangesAsync(CancellationToken.None);
                    Trace("FinalizeChangesSaved", currentOperation.Id);
                    Trace("CommitTransaction", currentOperation.Id);
                    await transaction.CommitAsync(CancellationToken.None);
                    Trace("TransactionCommitted", currentOperation.Id);
                    documentToArchive = document;
                    result = new(AccountingReceiptOutcome.Issued, currentOperation.Id, Map(document));
                }
                catch (Exception ex)
                {
                    lastExceptionStep = $"ExceptionAfter:{finalizeStep}";
                    logger?.LogWarning(ex, "Receipt finalization attempt failed for operation {OperationId} at step {Step}; execution strategy may retry.", operation.Id, lastExceptionStep);
                    try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                    db.ChangeTracker.Clear();
                    throw;
                }
            });

            if (documentToArchive is not null)
            {
                Trace("ArchiveBestEffort", operation.Id, $"DocumentId={documentToArchive.Id}");
                await TryArchiveBestEffortAsync(documentToArchive, userId);
                Trace("ArchiveBestEffortCompleted", operation.Id, $"DocumentId={documentToArchive.Id}");
            }
            return result ?? new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
        }
        catch (Exception ex)
        {
            var failedStep = lastExceptionStep ?? $"ExceptionAfter:{finalizeStep}";
            logger?.LogError(ex, "Failed to finalize Rivhit receipt operation {OperationId} at step {Step}.", operation.Id, failedStep);
            db.ChangeTracker.Clear();
            try
            {
                var failedOperation = await db.ExternalAccountingOperations.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(item => item.Id == operation.Id, CancellationToken.None);
                if (failedOperation is null)
                    return new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
                if (failedOperation.Status == ExternalAccountingOperationStatus.Completed)
                    return await ExistingResultAsync(failedOperation, CancellationToken.None);
                failedOperation.Status = ExternalAccountingOperationStatus.ReconciliationRequired;
                failedOperation.ProviderResponseCode = "ClientaFinalizationPending";
                failedOperation.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                return new(AccountingReceiptOutcome.ReconciliationRequired, failedOperation.Id);
            }
            catch (Exception stateException)
            {
                logger?.LogError(stateException, "Could not persist the reconciliation-required state for receipt operation {OperationId}.", operation.Id);
                return new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
            }
        }
    }

    private async Task<int?> FindTrustedCustomerIdAsync(Guid tenantId, Guid invoiceId, CancellationToken ct) =>
        await db.ExternalAccountingDocuments.AsNoTracking().Where(item => item.TenantId == tenantId && item.InvoiceId == invoiceId
            && item.Provider == ProviderKind && item.DocumentType == ExternalAccountingPurpose.Invoice
            && item.Status == ExternalAccountingDocumentStatus.Issued && item.ExternalCustomerId.HasValue)
            .OrderByDescending(item => item.IssuedAt).Select(item => item.ExternalCustomerId).FirstOrDefaultAsync(ct);

    private async Task<bool> LockInvoiceAsync(Guid tenantId, Guid invoiceId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return await db.Invoices.AnyAsync(item => item.TenantId == tenantId && item.Id == invoiceId, ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        command.CommandText = "SELECT \"Id\" FROM \"Invoices\" WHERE \"Id\" = @invoiceId AND \"TenantId\" = @tenantId FOR UPDATE";
        command.Parameters.AddWithValue("invoiceId", invoiceId);
        command.Parameters.AddWithValue("tenantId", tenantId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static PaymentMethod? ParseV1Method(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "cash" => PaymentMethod.Cash,
        "bit" => PaymentMethod.Bit,
        "paybox" => PaymentMethod.PayBox,
        "bank_transfer" => PaymentMethod.BankTransfer,
        _ => null
    };

    // Bank transfer has no safe hard-coded Rivhit type; it must be resolved from Payment.TypeList.
    private async Task<int> ResolveRivhitPaymentTypeAsync(PaymentMethod method, string credential, CancellationToken ct) => method switch
    {
        // Rivhit payment type IDs confirmed by the provider integration contract.
        PaymentMethod.Cash => 2,
        PaymentMethod.Bit => 332,
        PaymentMethod.PayBox => 333,
        PaymentMethod.BankTransfer => await ResolveBankTransferTypeAsync(credential, ct),
        // Credit, Check and Other have no verified safe Rivhit mapping. Return 0 so callers
        // persist a controlled rejection before invoking Receipt.New.
        _ => 0
    };

    private async Task<int> ResolveBankTransferTypeAsync(string credential, CancellationToken ct)
    {
        IReadOnlyList<ProviderPaymentType> types;
        try
        {
            types = await provider.GetPaymentTypesAsync(credential, ct);
        }
        catch
        {
            return 0;
        }
        var match = types.FirstOrDefault(t =>
            t.TypeCode is 4
            || (t.PaymentName?.Contains("bank", StringComparison.OrdinalIgnoreCase) ?? false)
            || (t.PaymentName?.Contains("העברה") ?? false));
        return match?.PaymentType ?? 0;
    }

    private static int? TryGetSafeClientIdNumber(string? value, out string? issue)
    {
        issue = null;
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed[0] == '0') { issue = "LeadingZero"; return null; }
        if (!trimmed.All(char.IsAsciiDigit)) { issue = "NonDigit"; return null; }
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result <= 0)
        { issue = "OutOfRange"; return null; }
        return result;
    }

    private static string BuildRequestReference(Guid tenantId, Guid invoiceId, string key)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}:{invoiceId:N}:{key}"))).ToLowerInvariant();
        return $"CLIENTA-RIVHIT-RECEIPT-{digest}";
    }

    private static bool IsComplete(ProviderReceiptDocument? document, decimal amount) => document is not null
        && document.DocumentType == 1 && document.DocumentNumber.HasValue && !string.IsNullOrWhiteSpace(document.DocumentIdentity)
        && document.Amount.HasValue && Math.Abs(document.Amount.Value - amount) <= Tolerance;

    private static bool HasConfirmedProviderDocument(ExternalAccountingOperation operation) => operation.ExternalDocumentType == NormalReceiptType
        && operation.ExternalDocumentNumber.HasValue && !string.IsNullOrWhiteSpace(operation.ExternalDocumentId)
        && operation.ExternalAmount.HasValue && Math.Abs(operation.ExternalAmount.Value - operation.Amount) <= Tolerance;

    private static void ApplyProviderDocument(ExternalAccountingOperation operation, ProviderReceiptDocument document)
    {
        operation.ExternalDocumentType = document.DocumentType;
        operation.ExternalDocumentNumber = document.DocumentNumber;
        operation.ExternalCustomerId = document.CustomerId;
        operation.ExternalDocumentId = document.DocumentIdentity;
        operation.ExternalAmount = document.Amount;
        operation.ProviderResponseCode = SanitizeCode(document.ErrorCode);
        operation.UpdatedAt = DateTime.UtcNow;
    }

    private async Task RejectAsync(ExternalAccountingOperation operation, string safeCode)
    {
        operation.Status = ExternalAccountingOperationStatus.Rejected;
        operation.ProviderResponseCode = safeCode;
        operation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Best-effort PDF archival of an Issued Rivhit receipt. Never alters the issuance outcome:
    /// any failure leaves the document Issued and only updates the independent ArchiveStatus.
    /// </summary>
    private async Task TryArchiveBestEffortAsync(ExternalAccountingDocument document, Guid? userId)
    {
        if (archive is null || userId is not Guid archiveUserId || archiveUserId == Guid.Empty) return;
        try
        {
            await archive.ArchiveAsync(document.TenantId, document.Id, archiveUserId, CancellationToken.None);
        }
        catch
        {
            // Intentionally swallowed: archive must never affect the already-Issued document.
        }
    }

    private async Task<AccountingReceiptResult> ExistingResultAsync(
        ExternalAccountingOperation operation,
        CancellationToken ct) =>
        operation.Status == ExternalAccountingOperationStatus.Completed
            ? new(
                AccountingReceiptOutcome.AlreadyCompleted,
                operation.Id,
                await db.ExternalAccountingDocuments
                    .AsNoTracking()
                    .Where(item =>
                        item.TenantId == operation.TenantId
                        && item.PaymentId == operation.PaymentId
                        && item.DocumentType == ExternalAccountingPurpose.Receipt)
                    .Select(item => new ExternalAccountingDocumentResponse(
                        item.Id,
                        item.InvoiceId,
                        item.Provider.ToString(),
                        item.DocumentType.ToString(),
                        item.Status.ToString(),
                        item.RequestReference,
                        item.ExternalDocumentType,
                        item.ExternalDocumentId,
                        item.ExternalDocumentNumber,
                        item.ExternalCustomerId,
                        item.PaymentId,
                        item.IssuedAt,
                        item.Currency,
                        item.Amount,
                        item.ExternalAmount,
                        item.ProviderResponseCode,
                        item.DeliveryChannel.ToString(),
                        item.DeliveryStatus.ToString(),
                        item.DeliveryAttemptedAt,
                        item.DeliveryErrorCode,
                        item.ArchiveStatus.ToString(),
                        item.ArchiveAttemptedAt,
                        item.ArchiveErrorCode,
                        item.CreatedAt,
                        item.UpdatedAt))
                    .FirstOrDefaultAsync(ct))
            : operation.Status == ExternalAccountingOperationStatus.Rejected
                ? new(AccountingReceiptOutcome.ProviderRejected, operation.Id)
                : new(AccountingReceiptOutcome.ReconciliationRequired, operation.Id);
    private static string? SanitizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var value = new string(
            code.Where(character =>
                    char.IsAsciiLetterOrDigit(character) ||
                    character is '_' or '-')
                .Take(50)
                .ToArray());

        return value.Length == 0 ? null : value;
    }

    private static ExternalAccountingDocumentResponse Map(ExternalAccountingDocument document) => new(document.Id, document.InvoiceId,
        document.Provider.ToString(), document.DocumentType.ToString(), document.Status.ToString(), document.RequestReference,
        document.ExternalDocumentType, document.ExternalDocumentId, document.ExternalDocumentNumber, document.ExternalCustomerId, document.PaymentId,
        document.IssuedAt, document.Currency, document.Amount, document.ExternalAmount,document.ProviderResponseCode,document.DeliveryChannel.ToString(),
        document.DeliveryStatus.ToString(),document.DeliveryAttemptedAt,document.DeliveryErrorCode,document.ArchiveStatus.ToString(),document.ArchiveAttemptedAt,
        document.ArchiveErrorCode,document.CreatedAt,document.UpdatedAt);
    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public sealed class IssueAccountingReceiptRequest
{
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    public AccountingDeliveryChannel DeliveryChannel { get; set; }
        = AccountingDeliveryChannel.None;

    /// <summary>
    /// When set, the receipt is issued for an existing verified CLIENTA Payment instead of
    /// reserving invoice balance and creating a new Payment. The operation is linked to this
    /// Payment from the start and FinalizeAsync never creates another Payment.
    /// </summary>
    public Guid? PaymentId { get; set; }
}