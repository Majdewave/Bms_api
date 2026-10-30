using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public enum ExternalAccountingDocumentStatus
{
    Issuing = 1,
    Issued = 2,
    Rejected = 3,
    ReconciliationRequired = 4
}

public enum ExternalAccountingPurpose
{
    Invoice = 1,
    Receipt = 2,
    CombinedInvoiceReceipt = 3
}

public enum AccountingDeliveryChannel
{
    None = 1,
    Email = 2
}

public enum AccountingDeliveryStatus
{
    NotRequested = 1,
    Requested = 2,
    Failed = 3
}

public enum AccountingArchiveStatus
{
    Pending = 1,
    Archived = 2,
    Failed = 3
}

public class ExternalAccountingDocument : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public IntegrationProvider Provider { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid? PaymentId { get; set; }
    public ExternalAccountingPurpose DocumentType { get; set; }
    public int? ExternalDocumentType { get; set; }
    public ExternalAccountingDocumentStatus Status { get; set; }

    [MaxLength(100)]
    public string RequestReference { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ExternalDocumentId { get; set; }

    public int? ExternalDocumentNumber { get; set; }
    public int? ExternalCustomerId { get; set; }
    public DateTime? IssuedAt { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "ILS";

    public decimal Amount { get; set; }
    public decimal? ExternalAmount { get; set; }

    [MaxLength(50)]
    public string? ProviderResponseCode { get; set; }

    public AccountingDeliveryChannel DeliveryChannel { get; set; } = AccountingDeliveryChannel.None;
    public AccountingDeliveryStatus DeliveryStatus { get; set; } = AccountingDeliveryStatus.NotRequested;
    public DateTime? DeliveryAttemptedAt { get; set; }

    [MaxLength(50)]
    public string? DeliveryErrorCode { get; set; }

    public AccountingArchiveStatus ArchiveStatus { get; set; } = AccountingArchiveStatus.Pending;
    public DateTime? ArchiveAttemptedAt { get; set; }

    [MaxLength(50)]
    public string? ArchiveErrorCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
