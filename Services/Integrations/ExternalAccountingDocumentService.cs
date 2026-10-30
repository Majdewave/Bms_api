using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Clienta.Api.Services.Integrations;

public enum AccountingDocumentOutcome
{
    Issued,
    AlreadyIssued,
    Issuing,
    ReconciliationRequired,
    InvoiceNotFound,
    UnsupportedTenant,
    IntegrationUnavailable,
    CredentialUnavailable,
    InvoiceStateUnsupported,
    CurrencyUnsupported,
    CustomerIdentifierRequired,
    CustomerIdentifierInvalid,
    InvoiceDataInvalid,
    VatMismatch,
    ProviderUnavailable,
    ProviderRejected,
    ProviderResponseMismatch,
    ReservationConflict
}

public sealed record ExternalAccountingDocumentResponse(
    Guid Id,
    Guid InvoiceId,
    string Provider,
    string Purpose,
    string Status,
    string RequestReference,
    int? ExternalDocumentType,
    string? ExternalDocumentId,
    int? ExternalDocumentNumber,
    int? ExternalCustomerId,
    Guid? PaymentId,
    DateTime? IssuedAt,
    string Currency,
    decimal Amount,
    decimal? ExternalAmount,
    string? ProviderResponseCode,
    string DeliveryChannel,
    string DeliveryStatus,
    DateTime? DeliveryAttemptedAt,
    string? DeliveryErrorCode,
    string ArchiveStatus,
    DateTime? ArchiveAttemptedAt,
    string? ArchiveErrorCode,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AccountingDocumentOperationResult(AccountingDocumentOutcome Outcome, ExternalAccountingDocumentResponse? Document);
public sealed class IssueAccountingInvoiceRequest
{
    public AccountingDeliveryChannel DeliveryChannel { get; set; }
        = AccountingDeliveryChannel.None;
}

public interface IExternalAccountingDocumentService
{
    Task<ExternalAccountingDocumentResponse?> GetInvoiceDocumentAsync(Guid tenantId, Guid invoiceId, CancellationToken ct);
    Task<IReadOnlyList<ExternalAccountingDocumentResponse>> GetTenantDocumentsAsync(Guid tenantId, CancellationToken ct);
    Task<AccountingDocumentOperationResult> IssueInvoiceAsync(Guid tenantId, Guid invoiceId, IssueAccountingInvoiceRequest request, CancellationToken ct);
}

public sealed class ExternalAccountingDocumentService : IExternalAccountingDocumentService
{
    private const IntegrationProvider Provider = IntegrationProvider.Rivhit;
    private const ExternalAccountingPurpose Purpose = ExternalAccountingPurpose.Invoice;
    private const int UnpaidInvoiceDocumentType = 1;
    private const decimal MoneyTolerance = 0.01m;

    private readonly AppDbContext _db;
    private readonly ITenantProductService _products;
    private readonly ITenantIntegrationService _integrations;
    private readonly IFeatureService _features;
    private readonly IAccountingDocumentProvider _provider;
    private readonly IExternalAccountingArchiveService? _archive;
    private readonly IHostEnvironment? _hostEnvironment;
    private readonly ILogger<ExternalAccountingDocumentService>? _logger;

    public ExternalAccountingDocumentService(
        AppDbContext db,
        ITenantProductService products,
        ITenantIntegrationService integrations,
        IFeatureService features,
        IAccountingDocumentProvider provider,
        IExternalAccountingArchiveService? archive = null,
        IHostEnvironment? hostEnvironment = null,
        ILogger<ExternalAccountingDocumentService>? logger = null)
    {
        _db = db;
        _products = products;
        _integrations = integrations;
        _features = features;
        _provider = provider;
        _archive = archive;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task<ExternalAccountingDocumentResponse?> GetInvoiceDocumentAsync(Guid tenantId, Guid invoiceId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || invoiceId == Guid.Empty) return null;
        var entity = await _db.ExternalAccountingDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.InvoiceId == invoiceId && x.Provider == Provider && x.DocumentType == Purpose, ct);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<ExternalAccountingDocumentResponse>> GetTenantDocumentsAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || await _products.GetAsync(tenantId, ct) != ResolvedTenantProduct.Business)
            return Array.Empty<ExternalAccountingDocumentResponse>();
        var rows = await _db.ExternalAccountingDocuments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Provider == Provider)
            .ToListAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<AccountingDocumentOperationResult> IssueInvoiceAsync(Guid tenantId, Guid invoiceId, IssueAccountingInvoiceRequest request, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || await _products.GetAsync(tenantId, ct) != ResolvedTenantProduct.Business)
            return new(AccountingDocumentOutcome.UnsupportedTenant, null);
        if (!await _features.IsEnabledAsync("invoices"))
            return new(AccountingDocumentOutcome.UnsupportedTenant, null);
        if (request.DeliveryChannel is not (AccountingDeliveryChannel.None or AccountingDeliveryChannel.Email))
        {
            return new(AccountingDocumentOutcome.InvoiceDataInvalid, null);
        }

        var existing = await _db.ExternalAccountingDocuments.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Provider == Provider && x.InvoiceId == invoiceId && x.DocumentType == Purpose, ct);
        if (existing is not null)
        {
            if (existing.Status is ExternalAccountingDocumentStatus.Issuing or ExternalAccountingDocumentStatus.ReconciliationRequired)
                return await RecoverOutstandingAsync(existing, tenantId, ct);
            return existing.Status switch
            {
                ExternalAccountingDocumentStatus.Issued => new(AccountingDocumentOutcome.AlreadyIssued, Map(existing)),
                ExternalAccountingDocumentStatus.Issuing => new(AccountingDocumentOutcome.Issuing, Map(existing)),
                ExternalAccountingDocumentStatus.ReconciliationRequired => new(AccountingDocumentOutcome.ReconciliationRequired, Map(existing)),
                _ => new(AccountingDocumentOutcome.ProviderRejected, Map(existing))
            };
        }

        var integration = await _db.TenantIntegrations.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Provider == Provider, ct);
        if (integration is null || !integration.IsEnabled || !integration.AccountingEnabled)
            return new(AccountingDocumentOutcome.IntegrationUnavailable, null);

        var credential = await _integrations.GetOperationalCredentialAsync(tenantId, Provider, ct);
        if (string.IsNullOrWhiteSpace(credential))
            return new(AccountingDocumentOutcome.CredentialUnavailable, null);

        var invoice = await _db.Invoices.AsNoTracking()
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId, ct);
        if (invoice is null)
            return new(AccountingDocumentOutcome.InvoiceNotFound, null);
        if (invoice.Status != InvoiceStatus.Pending)
            return new(AccountingDocumentOutcome.InvoiceStateUnsupported, null);
        if (invoice.WithholdingTaxRate != 0m || invoice.WithholdingTaxAmount != 0m)
            return new(AccountingDocumentOutcome.InvoiceDataInvalid, null);
        if (!string.Equals((await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Currency).FirstOrDefaultAsync(ct))?.Trim(), "ILS", StringComparison.OrdinalIgnoreCase))
            return new(AccountingDocumentOutcome.CurrencyUnsupported, null);
        var client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.ClientId && c.TenantId == tenantId, ct);
        if (client is null)
            return new(AccountingDocumentOutcome.InvoiceDataInvalid, null);
        string? deliveryEmail = null;

        if (request.DeliveryChannel == AccountingDeliveryChannel.Email)
        {
            deliveryEmail = client.Email?.Trim();

            if (string.IsNullOrWhiteSpace(deliveryEmail))
                return new(AccountingDocumentOutcome.InvoiceDataInvalid, null);
        }
        var clientIdNumber = TryGetSafeClientIdNumber(client.IdNumber, out var idNumberIssue);

        var existingCustomerId = await FindExistingCustomerIdAsync(tenantId, invoice.ClientId, ct);
        if (!existingCustomerId.HasValue && !clientIdNumber.HasValue)
            return new(idNumberIssue is not null ? AccountingDocumentOutcome.CustomerIdentifierInvalid : AccountingDocumentOutcome.CustomerIdentifierRequired, null);

        var lineItems = invoice.LineItems.OrderBy(i => i.Id).ToArray();
        if (lineItems.Length == 0 || lineItems.Any(i => string.IsNullOrWhiteSpace(i.Description) || i.Quantity <= 0 || i.Price < 0))
            return new(AccountingDocumentOutcome.InvoiceDataInvalid, null);
        if (invoice.VatRate <= 0m || invoice.VatRate > 100m)
            return new(AccountingDocumentOutcome.InvoiceDataInvalid, null);
        if (lineItems.Any(i => i.Price == 0m))
            return new(AccountingDocumentOutcome.InvoiceDataInvalid, null);

        var lineSubtotal = decimal.Round(lineItems.Sum(i => i.Quantity * i.Price), 2, MidpointRounding.AwayFromZero);
        var expectedVat = decimal.Round(lineSubtotal * invoice.VatRate / 100m, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(lineSubtotal - invoice.Subtotal) > MoneyTolerance
            || Math.Abs(expectedVat - invoice.VatAmount) > MoneyTolerance
            || Math.Abs(invoice.Subtotal + invoice.VatAmount - invoice.TotalAmount) > MoneyTolerance
            || Math.Abs(invoice.TotalAmount - invoice.FinalAmountToPay) > MoneyTolerance)
            return new(AccountingDocumentOutcome.InvoiceDataInvalid, null);

        decimal providerVat;
        try
        {
            var types = await _provider.GetDocumentTypesAsync(credential, ct);
            var type = types.SingleOrDefault(x => x.DocumentType == UnpaidInvoiceDocumentType);
            if (type is null || type.IsAccounting != true || type.IsInvoiceReceipt != false || type.PriceIncludesVat != false)
                return new(AccountingDocumentOutcome.ProviderUnavailable, null);

            providerVat = await _provider.GetVatRateAsync(credential, ct);
            if (Math.Abs(providerVat - invoice.VatRate) > 0.0001m)
                return new(AccountingDocumentOutcome.VatMismatch, null);
        }
        catch (RivhitProviderException)
        {
            return new(AccountingDocumentOutcome.ProviderUnavailable, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(AccountingDocumentOutcome.ProviderUnavailable, null);
        }

        var requestReference = $"CLIENTA-RIVHIT-INVOICE-{invoice.Id:N}";
        var reservation = new ExternalAccountingDocument
        {
            TenantId = tenantId,
            Provider = Provider,
            InvoiceId = invoice.Id,
            DocumentType = Purpose,
            ExternalDocumentType = UnpaidInvoiceDocumentType,
            Status = ExternalAccountingDocumentStatus.Issuing,
            RequestReference = requestReference,
            Currency = "ILS",
            Amount = invoice.TotalAmount,

            DeliveryChannel = request.DeliveryChannel,
            DeliveryStatus = AccountingDeliveryStatus.NotRequested,
            DeliveryAttemptedAt = null,
            DeliveryErrorCode = null,

            ArchiveStatus = AccountingArchiveStatus.Pending,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ExternalAccountingDocuments.Add(reservation);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.Entry(reservation).State = EntityState.Detached;
            var winner = await _db.ExternalAccountingDocuments.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.Provider == Provider && x.InvoiceId == invoiceId && x.DocumentType == Purpose, ct);
            if (winner is null)
                return new(AccountingDocumentOutcome.ReservationConflict, null);
            return ExistingResult(winner);
        }

        var issueRequest = new AccountingInvoiceIssueRequest(
            invoice.Id,
            invoice.InvoiceNumber,
            InvoiceCalendarDate.FromStorageDateTime(invoice.InvoiceDate),
            invoice.DueDate.HasValue ? InvoiceCalendarDate.FromStorageDateTime(invoice.DueDate.Value) : null,
            client.FullName,
            client.Address,
            client.Phone,
            clientIdNumber,
            existingCustomerId,
            idNumberIssue,
            invoice.VatRate,
            invoice.TotalAmount,
            lineItems.Select(i => new AccountingInvoiceLineRequest(i.Description.Trim(),i.Quantity,i.Price)).ToArray(), requestReference,SendEmail: request.DeliveryChannel == AccountingDeliveryChannel.Email,
            EmailTo: deliveryEmail);

        ProviderMutationResult providerResult;
        try
        {
            if (_hostEnvironment?.IsDevelopment() == true)
            {
                _logger?.LogInformation(
                    "[RIVHIT DEV Document.New] VAT metadata used for request RequestReference={RequestReference} ProviderVatRate={ProviderVatRate} InvoiceVatRate={InvoiceVatRate}",
                    requestReference,
                    providerVat,
                    invoice.VatRate);
            }

            providerResult = await _provider.IssueInvoiceAsync(credential, issueRequest, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A client disconnect does not prove Rivhit did not process the transmitted request.
            providerResult = new(ProviderMutationDisposition.Unknown, null);
        }
        catch
        {
            providerResult = new(ProviderMutationDisposition.Unknown, null);
        }

        if (providerResult.Disposition == ProviderMutationDisposition.Unknown)
        {
            var recovered = await RecoverAsync(credential, requestReference, CancellationToken.None);
            if (recovered.Status == ProviderRequestStatus.Success && recovered.Document is not null)
                providerResult = new(ProviderMutationDisposition.Success, recovered.Document);
            else if (recovered.Status == ProviderRequestStatus.Rejected)
                providerResult = new(ProviderMutationDisposition.Rejected, recovered.Document);
            else
            {
                reservation.Status = ExternalAccountingDocumentStatus.ReconciliationRequired;
                reservation.ProviderResponseCode = "ReconciliationRequired";
                reservation.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
                return new(AccountingDocumentOutcome.ReconciliationRequired, Map(reservation));
            }
        }

        if (providerResult.Disposition == ProviderMutationDisposition.Rejected)
        {
            reservation.Status = ExternalAccountingDocumentStatus.Rejected;
            reservation.ProviderResponseCode = SanitizeCode(providerResult.Document?.ErrorCode) ?? "ProviderRejected";
            reservation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new(AccountingDocumentOutcome.ProviderRejected, Map(reservation));
        }

        var document = providerResult.Document;
        if (document is null || document.DocumentType != UnpaidInvoiceDocumentType || document.DocumentNumber is null || string.IsNullOrWhiteSpace(document.DocumentIdentity))
        {
            reservation.Status = ExternalAccountingDocumentStatus.ReconciliationRequired;
            reservation.ProviderResponseCode = "IncompleteProviderResponse";
            reservation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new(AccountingDocumentOutcome.ReconciliationRequired, Map(reservation));
        }

        if (!document.Amount.HasValue)
        {
            var details = await _provider.GetDocumentDetailsAsync(credential, document.DocumentType, document.DocumentNumber.Value, CancellationToken.None);
            if (details?.Total is decimal detailedTotal)
                document = document with { Amount = detailedTotal };
        }

        if (document.Amount.HasValue && Math.Abs(document.Amount.Value - invoice.TotalAmount) > MoneyTolerance)
        {
            reservation.Status = ExternalAccountingDocumentStatus.ReconciliationRequired;
            reservation.ExternalDocumentType = document.DocumentType;
            reservation.ExternalDocumentNumber = document.DocumentNumber;
            reservation.ExternalDocumentId = document.DocumentIdentity;
            reservation.ExternalCustomerId = document.CustomerId;
            reservation.ExternalAmount = document.Amount;
            reservation.ProviderResponseCode = "AmountMismatch";
            reservation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new(AccountingDocumentOutcome.ProviderResponseMismatch, Map(reservation));
        }

        reservation.Status = ExternalAccountingDocumentStatus.Issued;
        reservation.ExternalDocumentType = document.DocumentType;
        reservation.ExternalDocumentNumber = document.DocumentNumber;
        reservation.ExternalDocumentId = document.DocumentIdentity;
        reservation.ExternalCustomerId = document.CustomerId;
        reservation.ExternalAmount = document.Amount;
        reservation.IssuedAt = DateTime.UtcNow;
        reservation.ProviderResponseCode = document.ErrorCode;
        reservation.DeliveryStatus = reservation.DeliveryChannel == AccountingDeliveryChannel.Email
        ? AccountingDeliveryStatus.Requested
        : AccountingDeliveryStatus.NotRequested;

        reservation.DeliveryAttemptedAt =
            reservation.DeliveryChannel == AccountingDeliveryChannel.Email
                ? DateTime.UtcNow
                : null;

        reservation.DeliveryErrorCode = null;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        await TryArchiveBestEffortAsync(reservation);
        return new(AccountingDocumentOutcome.Issued, Map(reservation));
    }

    /// <summary>
    /// Best-effort PDF archival of an Issued Rivhit document. Never alters issuance outcome:
    /// any failure leaves Status = Issued and only updates the independent ArchiveStatus/ArchiveErrorCode.
    /// No retry of Document.New/Receipt.New is triggered here (archive uses read-only Copy + PDF GET).
    /// </summary>
    private async Task TryArchiveBestEffortAsync(ExternalAccountingDocument document)
    {
        if (_archive is null) return;
        try
        {
            await _archive.ArchiveAsync(document.TenantId, document.Id, Guid.Empty, CancellationToken.None);
        }
        catch
        {
            // Intentionally swallowed: archive must never affect the already-Issued document.
        }
    }

    private async Task<ProviderRequestRecovery> RecoverAsync(string credential, string requestReference, CancellationToken ct)
    {
        var last = await _provider.GetLastRequestAsync(credential, requestReference, ct);
        if (last.Status is ProviderRequestStatus.Success or ProviderRequestStatus.Rejected)
            return last;

        var all = await _provider.GetAllRequestsAsync(credential, requestReference, ct);
        var matching = all.LastOrDefault(x => x.Status is ProviderRequestStatus.Success or ProviderRequestStatus.Rejected);
        return matching ?? (all.Any(x => x.Status == ProviderRequestStatus.Unknown)
            ? new ProviderRequestRecovery(ProviderRequestStatus.Unknown, null)
            : last);
    }

    private async Task<AccountingDocumentOperationResult> RecoverOutstandingAsync(ExternalAccountingDocument row, Guid tenantId, CancellationToken ct)
    {
        var credential = await _integrations.GetOperationalCredentialAsync(tenantId, Provider, ct);
        if (string.IsNullOrWhiteSpace(credential))
            return new(AccountingDocumentOutcome.ReconciliationRequired, Map(row));

        var recovered = await RecoverAsync(credential, row.RequestReference, CancellationToken.None);
        if (recovered.Status == ProviderRequestStatus.Success && recovered.Document is not null)
        {
            var document = recovered.Document;

            if (document.DocumentType != UnpaidInvoiceDocumentType
                || !document.DocumentNumber.HasValue
                || string.IsNullOrWhiteSpace(document.DocumentIdentity))
                return new(AccountingDocumentOutcome.ReconciliationRequired, Map(row));

            if (!document.Amount.HasValue)
            {
                var details = await _provider.GetDocumentDetailsAsync(
                    credential,
                    document.DocumentType,
                    document.DocumentNumber.Value,
                    CancellationToken.None);

                if (details?.Total is decimal total)
                    document = document with { Amount = total };
            }

            if (!document.Amount.HasValue
                || Math.Abs(document.Amount.Value - row.Amount) > MoneyTolerance)
                return new(AccountingDocumentOutcome.ReconciliationRequired, Map(row));

            ApplyIssuedDocument(row, document);
            await _db.SaveChangesAsync(CancellationToken.None);
            return new(AccountingDocumentOutcome.AlreadyIssued, Map(row));
        }
        if (recovered.Status == ProviderRequestStatus.Rejected)
        {
            row.Status = ExternalAccountingDocumentStatus.Rejected;
            row.ProviderResponseCode = SanitizeCode(recovered.Document?.ErrorCode) ?? "ProviderRejected";
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new(AccountingDocumentOutcome.ProviderRejected, Map(row));
        }

        if (recovered.Status == ProviderRequestStatus.NotFound)
        {
            // Provider definitively has no record for this reference: release the reservation so a
            // fresh issuance is allowed (aligned with the receipt flow's ProviderNotReceived handling).
            row.Status = ExternalAccountingDocumentStatus.Rejected;
            row.ProviderResponseCode = "ProviderNotReceived";
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new(AccountingDocumentOutcome.ProviderRejected, Map(row));
        }

        row.Status = ExternalAccountingDocumentStatus.ReconciliationRequired;
        row.ProviderResponseCode = "ReconciliationRequired";
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        return new(AccountingDocumentOutcome.ReconciliationRequired, Map(row));
    }

    private async Task<int?> FindExistingCustomerIdAsync(Guid tenantId, Guid clientId, CancellationToken ct)
    {
        return await (from external in _db.ExternalAccountingDocuments.AsNoTracking()
                      join invoice in _db.Invoices.AsNoTracking() on new { external.InvoiceId, external.TenantId } equals new { InvoiceId = invoice.Id, invoice.TenantId }
                      where external.TenantId == tenantId && invoice.ClientId == clientId && external.Provider == Provider
                          && external.Status == ExternalAccountingDocumentStatus.Issued && external.ExternalCustomerId.HasValue
                      orderby external.IssuedAt descending
                      select external.ExternalCustomerId).FirstOrDefaultAsync(ct);
    }

    private static int? TryGetSafeClientIdNumber(string? idNumber, out string? issue)
    {
        issue = null;
        if (string.IsNullOrWhiteSpace(idNumber)) return null;
        var value = idNumber.Trim();
        if (value.Length == 0) return null;
        if (value[0] == '0') { issue = "LeadingZero"; return null; }
        if (value.Any(c => c is < '0' or > '9')) { issue = "NonDigit"; return null; }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        { issue = "OutOfRange"; return null; }
        return parsed;
    }

    private static AccountingDocumentOperationResult ExistingResult(ExternalAccountingDocument row) => row.Status switch
    {
        ExternalAccountingDocumentStatus.Issued => new(AccountingDocumentOutcome.AlreadyIssued, Map(row)),
        ExternalAccountingDocumentStatus.Issuing => new(AccountingDocumentOutcome.Issuing, Map(row)),
        ExternalAccountingDocumentStatus.ReconciliationRequired => new(AccountingDocumentOutcome.ReconciliationRequired, Map(row)),
        _ => new(AccountingDocumentOutcome.ProviderRejected, Map(row))
    };

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string? SanitizeCode(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Length <= 50 && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? code : "ProviderRejected";

    private static ExternalAccountingDocumentResponse Map(ExternalAccountingDocument x) => new(
        x.Id, x.InvoiceId, x.Provider.ToString(), x.DocumentType.ToString(), x.Status.ToString(), x.RequestReference,
        x.ExternalDocumentType, x.ExternalDocumentId, x.ExternalDocumentNumber, x.ExternalCustomerId, x.PaymentId, x.IssuedAt,
        x.Currency,x.Amount,x.ExternalAmount,x.ProviderResponseCode,x.DeliveryChannel.ToString(),x.DeliveryStatus.ToString(),
        x.DeliveryAttemptedAt,x.DeliveryErrorCode,x.ArchiveStatus.ToString(),x.ArchiveAttemptedAt,x.ArchiveErrorCode,x.CreatedAt,x.UpdatedAt);

    private static void ApplyIssuedDocument(ExternalAccountingDocument row, ProviderInvoiceDocument document)
    {
        row.Status = ExternalAccountingDocumentStatus.Issued;
        row.ExternalDocumentType = document.DocumentType;
        row.ExternalDocumentNumber = document.DocumentNumber;
        row.ExternalDocumentId = document.DocumentIdentity;
        row.ExternalCustomerId = document.CustomerId;
        row.ExternalAmount = document.Amount;
        row.IssuedAt = DateTime.UtcNow;
        row.ProviderResponseCode = SanitizeCode(document.ErrorCode);
        row.DeliveryStatus = row.DeliveryChannel == AccountingDeliveryChannel.Email
        ? AccountingDeliveryStatus.Requested
        : AccountingDeliveryStatus.NotRequested;

        row.DeliveryAttemptedAt =
            row.DeliveryChannel == AccountingDeliveryChannel.Email
                ? DateTime.UtcNow
                : null;

        row.DeliveryErrorCode = null;
        row.UpdatedAt = DateTime.UtcNow;
    }
}
