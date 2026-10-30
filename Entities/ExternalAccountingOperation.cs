using System.ComponentModel.DataAnnotations;
using Clienta.Api.Models;

namespace Clienta.Api.Entities;

public enum ExternalAccountingOperationType
{
    Receipt = 1
}

public enum ExternalAccountingOperationStatus
{
    Issuing = 1,
    Finalizing = 2,
    Rejected = 3,
    ReconciliationRequired = 4,
    Completed = 5
}

/// <summary>Durable provider operation/reservation, distinct from both a provider document and CLIENTA Payment.</summary>
public sealed class ExternalAccountingOperation : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public IntegrationProvider Provider { get; set; } = IntegrationProvider.Rivhit;
    public Guid InvoiceId { get; set; }
    public ExternalAccountingOperationType OperationType { get; set; } = ExternalAccountingOperationType.Receipt;
    public ExternalAccountingOperationStatus Status { get; set; } = ExternalAccountingOperationStatus.Issuing;
    public decimal Amount { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "ILS";
    public PaymentMethod PaymentMethod { get; set; }
    public AccountingDeliveryChannel DeliveryChannel { get; set; } = AccountingDeliveryChannel.None;
    public DateTime PaymentDate { get; set; }
    [MaxLength(100)] public string IdempotencyKey { get; set; } = string.Empty;
    [MaxLength(100)] public string RequestReference { get; set; } = string.Empty;
    public Guid? PaymentId { get; set; }
    public int? ExternalDocumentType { get; set; }
    [MaxLength(100)] public string? ExternalDocumentId { get; set; }
    public int? ExternalDocumentNumber { get; set; }
    public int? ExternalCustomerId { get; set; }
    public decimal? ExternalAmount { get; set; }
    [MaxLength(50)] public string? ProviderResponseCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}