using System.Text.Json.Serialization;

namespace Clienta.Api.Services.Integrations;

public sealed record AccountingInvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice);

public sealed record AccountingInvoiceIssueRequest(
    Guid InvoiceId,
    string InvoiceNumber,
    DateOnly IssueDate,
    DateOnly? DueDate,
    string ClientName,
    string? ClientAddress,
    string? ClientPhone,
    int? ClientIdNumber,
    int? ExistingCustomerId,
    /// <summary>Human-readable reason when ClientIdNumber was present but rejected (null when valid/absent).</summary>
    string? ClientIdNumberIssue,
    decimal VatRate,
    decimal TotalAmount,
    IReadOnlyList<AccountingInvoiceLineRequest> Lines,
    string RequestReference,
    bool SendEmail = false,
    string? EmailTo = null);

public sealed record AccountingReceiptIssueRequest(
    int ReceiptType,
    int CustomerId,
    string CustomerName,
    string? CustomerAddress,
    string? CustomerPhone,
    int? CustomerIdNumber,
    /// <summary>Human-readable reason when CustomerIdNumber was present but rejected (null when valid/absent).</summary>
    string? CustomerIdNumberIssue,
    DateOnly PaymentDate,
    decimal Amount,
    int PaymentType,
    string RequestReference,
    bool CreateCustomer,
    bool FindById,
    bool SendEmail = false,
    string? EmailTo = null);

/// <summary>Delivery-only outcome; never carries accounting meaning.</summary>
public sealed record ProviderDeliveryResult(ProviderMutationDisposition Disposition, string? SafeCode);

public interface IAccountingDocumentProvider : IAccountingIntegrationProvider
{
    Task<IReadOnlyList<ProviderDocumentType>> GetDocumentTypesAsync(string credential, CancellationToken cancellationToken);
    Task<decimal> GetVatRateAsync(string credential, CancellationToken cancellationToken);
    Task<ProviderMutationResult> IssueInvoiceAsync(string credential, AccountingInvoiceIssueRequest request, CancellationToken cancellationToken);
    Task<ProviderReceiptMutationResult> IssueReceiptAsync(string credential, AccountingReceiptIssueRequest request, CancellationToken cancellationToken);
    Task<ProviderDocumentDetails?> GetDocumentDetailsAsync(string credential, int documentType, int documentNumber, CancellationToken cancellationToken);
    Task<ProviderRequestRecovery> GetLastRequestAsync(string credential, string requestReference, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderRequestRecovery>> GetAllRequestsAsync(string credential, string requestReference, CancellationToken cancellationToken);
    Task<ProviderDeliveryResult> SendDocumentByEmailAsync(string credential, int documentType, int documentNumber, string email, CancellationToken cancellationToken);
    Task<ProviderDeliveryResult> SendReceiptByEmailAsync(string credential, int receiptType, int receiptNumber, string email, CancellationToken cancellationToken);
    Task<string?> GetDocumentCopyLinkAsync(string credential, int documentType, int documentNumber, CancellationToken cancellationToken);
    Task<string?> GetReceiptCopyLinkAsync(string credential, int receiptType, int receiptNumber, CancellationToken cancellationToken);
    /// <summary>Rivhit payment-type metadata (Payment.TypeList); used to resolve the correct type for non-cash methods.</summary>
    Task<IReadOnlyList<ProviderPaymentType>> GetPaymentTypesAsync(string credential, CancellationToken cancellationToken);
}

public sealed record ProviderReceiptDocument(
    int? DocumentType,
    int? DocumentNumber,
    int? CustomerId,
    string? DocumentIdentity,
    decimal? Amount,
    int? ConfirmationNumber,
    string? ErrorCode);

public sealed record ProviderReceiptMutationResult(ProviderMutationDisposition Disposition, ProviderReceiptDocument? Document);

public sealed record ProviderDocumentType(
    int DocumentType,
    string? Name,
    bool? IsInvoiceReceipt,
    bool? IsAccounting,
    bool? PriceIncludesVat);

public sealed record ProviderPaymentType(int PaymentType, string? PaymentName, int? TypeCode);

public sealed record ProviderReceiptType(
    int ReceiptType,
    string? ReceiptName,
    bool? IsInvoiceReceipt,
    int? PaymentType,
    int? StartNumber,
    int? OpenFormat,
    int? Clause46);

public interface IAccountingMetadataProvider
{
    Task<IReadOnlyList<ProviderPaymentType>> GetPaymentTypesAsync(string credential, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderReceiptType>> GetReceiptTypesAsync(string credential, CancellationToken cancellationToken);
}

public sealed record ProviderInvoiceDocument(
    int DocumentType,
    int? DocumentNumber,
    string? DocumentIdentity,
    int? CustomerId,
    decimal? Amount,
    string? ErrorCode);

public sealed record ProviderDocumentDetails(
    decimal? Total,
    decimal? TotalWithoutVat,
    decimal? TotalVat,
    string? IssueDate,
    string? DueDate,
    int? CurrencyId,
    bool? PriceIncludesVat);

public enum ProviderMutationDisposition
{
    Success,
    Rejected,
    Unknown
}

public sealed record ProviderMutationResult(ProviderMutationDisposition Disposition, ProviderInvoiceDocument? Document);

public enum ProviderRequestStatus
{
    Success,
    Rejected,
    NotFound,
    Unknown
}

public sealed record ProviderRequestRecovery(ProviderRequestStatus Status, ProviderInvoiceDocument? Document);

internal sealed class RivhitDocumentNewRequest
{
    [JsonPropertyName("api_token")] public required string ApiToken { get; init; }
    [JsonPropertyName("document_type")] public int DocumentType { get; init; }
    [JsonPropertyName("customer_id")] public int CustomerId { get; init; }
    [JsonPropertyName("last_name")] public required string LastName { get; init; }
    [JsonPropertyName("address")] public string? Address { get; init; }
    [JsonPropertyName("phone")] public string? Phone { get; init; }
    [JsonPropertyName("id_number")] public int? IdNumber { get; init; }
    [JsonPropertyName("currency_id")] public int CurrencyId { get; init; }
    [JsonPropertyName("exchange_rate")] public decimal ExchangeRate { get; init; }
    [JsonPropertyName("issue_date")] public required string IssueDate { get; init; }
    [JsonPropertyName("due_date")] public string? DueDate { get; init; }
    [JsonPropertyName("price_include_vat")] public bool PriceIncludesVat { get; init; }
    [JsonPropertyName("round_digits")] public int RoundDigits { get; init; }
    [JsonPropertyName("items")] public required IReadOnlyList<RivhitDocumentItem> Items { get; init; }
    [JsonPropertyName("create_customer")] public bool CreateCustomer { get; init; }
    [JsonPropertyName("find_by_id")] public bool FindById { get; init; }
    [JsonPropertyName("request_reference")] public required string RequestReference { get; init; }
    [JsonPropertyName("prevent_duplicates")] public bool PreventDuplicates { get; init; }
    [JsonPropertyName("send_mail")] public bool SendMail { get; init; }
    [JsonPropertyName("email_to")] public string? EmailTo { get; init; }
}

internal sealed class RivhitDocumentItem
{
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("quantity")] public decimal Quantity { get; init; }
    [JsonPropertyName("price_nis")] public decimal PriceNis { get; init; }
    [JsonPropertyName("exempt_vat")] public bool ExemptVat { get; init; }
}

internal sealed class RivhitReceiptNewRequest
{
    [JsonPropertyName("api_token")] public required string ApiToken { get; init; }
    [JsonPropertyName("receipt_type")] public int ReceiptType { get; init; }
    [JsonPropertyName("customer_id")] public int CustomerId { get; init; }
    [JsonPropertyName("last_name")] public required string LastName { get; init; }
    [JsonPropertyName("address")] public string? Address { get; init; }
    [JsonPropertyName("phone")] public string? Phone { get; init; }
    [JsonPropertyName("id_number")] public int? IdNumber { get; init; }
    [JsonPropertyName("issue_date")] public required string IssueDate { get; init; }
    [JsonPropertyName("currency_id")] public int CurrencyId { get; init; }
    [JsonPropertyName("exchange_rate")] public decimal ExchangeRate { get; init; }
    [JsonPropertyName("payments")] public required IReadOnlyList<RivhitReceiptPaymentRow> Payments { get; init; }
    [JsonPropertyName("request_reference")] public required string RequestReference { get; init; }
    [JsonPropertyName("prevent_duplicates")] public bool PreventDuplicates { get; init; }
    [JsonPropertyName("create_customer")] public bool CreateCustomer { get; init; }
    [JsonPropertyName("find_by_id")] public bool FindById { get; init; }
    [JsonPropertyName("send_mail")] public bool SendMail { get; init; }
    [JsonPropertyName("email_to")] public string? EmailTo { get; init; }
}

internal sealed class RivhitReceiptPaymentRow
{
    [JsonPropertyName("payment_type")] public int PaymentType { get; init; }
    [JsonPropertyName("amount_nis")] public decimal AmountNis { get; init; }
}

internal sealed record RivhitEnvelope<T>(
    [property: JsonPropertyName("error_code")] int? ErrorCode,
    [property: JsonPropertyName("client_message")] string? ClientMessage,
    [property: JsonPropertyName("data")] T? Data);

internal sealed record RivhitDocumentData(
    [property: JsonPropertyName("document_type")] int? DocumentType,
    [property: JsonPropertyName("document_number")] int? DocumentNumber,
    [property: JsonPropertyName("document_identity")] string? DocumentIdentity,
    [property: JsonPropertyName("customer_id")] int? CustomerId,
    [property: JsonPropertyName("amount")] decimal? Amount);

internal sealed record RivhitReceiptData(
    [property: JsonPropertyName("document_type")] int? DocumentType,
    [property: JsonPropertyName("document_number")] int? DocumentNumber,
    [property: JsonPropertyName("customer_id")] int? CustomerId,
    [property: JsonPropertyName("document_identity")] string? DocumentIdentity,
    [property: JsonPropertyName("amount")] decimal? Amount,
    [property: JsonPropertyName("confirmation_number")] int? ConfirmationNumber);

internal sealed record RivhitDocumentTypeData(
    [property: JsonPropertyName("document_type_list")] List<RivhitDocumentTypeDataItem>? DocumentTypes);

internal sealed record RivhitDocumentTypeDataItem(
    [property: JsonPropertyName("document_type")] int? DocumentType,
    [property: JsonPropertyName("document_name")] string? Name,
    [property: JsonPropertyName("is_invoice_receipt")] bool? IsInvoiceReceipt,
    [property: JsonPropertyName("is_accounting")] bool? IsAccounting,
    [property: JsonPropertyName("price_include_vat")] bool? PriceIncludesVat);

internal sealed record RivhitVatRateData([property: JsonPropertyName("vat_rate")] decimal? VatRate);

internal sealed record RivhitPaymentTypeListData(
    [property: JsonPropertyName("payment_type_list")] List<RivhitPaymentTypeDataItem>? PaymentTypes);

internal sealed record RivhitPaymentTypeDataItem(
    [property: JsonPropertyName("payment_type")] int? PaymentType,
    [property: JsonPropertyName("payment_name")] string? PaymentName,
    [property: JsonPropertyName("type_code")] int? TypeCode);

internal sealed record RivhitReceiptTypeListData(
    [property: JsonPropertyName("receipt_type_list")] List<RivhitReceiptTypeDataItem>? ReceiptTypes);

internal sealed record RivhitReceiptTypeDataItem(
    [property: JsonPropertyName("receipt_type")] int? ReceiptType,
    [property: JsonPropertyName("receipt_name")] string? ReceiptName,
    [property: JsonPropertyName("is_invoice_receipt")] bool? IsInvoiceReceipt,
    [property: JsonPropertyName("payment_type")] int? PaymentType,
    [property: JsonPropertyName("start_number")] int? StartNumber,
    [property: JsonPropertyName("open_format")] int? OpenFormat,
    [property: JsonPropertyName("clause46")] int? Clause46);

internal sealed record RivhitDocumentDetailsData(
    [property: JsonPropertyName("document_total")] decimal? Total,
    [property: JsonPropertyName("total_without_vat")] decimal? TotalWithoutVat,
    [property: JsonPropertyName("total_vat")] decimal? TotalVat,
    [property: JsonPropertyName("document_date")] string? IssueDate,
    [property: JsonPropertyName("document_due_date")] string? DueDate,
    [property: JsonPropertyName("currency_id")] int? CurrencyId,
    [property: JsonPropertyName("price_include_vat")] bool? PriceIncludesVat);
