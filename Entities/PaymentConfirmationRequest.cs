using Clienta.Api.Models;

namespace Clienta.Api.Entities;

public class PaymentConfirmationRequest : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? ProjectId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public decimal? RequestedAmount { get; set; }
    public string CurrencyCode { get; set; } = "ILS";
    public DateTime ExpiresAt { get; set; }
    public PaymentConfirmationRequestStatus Status { get; set; } = PaymentConfirmationRequestStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public Guid? SubmittedPaymentId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }

    public Tenant Tenant { get; set; } = default!;
    public Invoice Invoice { get; set; } = default!;
    public Client Client { get; set; } = default!;
    public Project? Project { get; set; }
    public Payment? SubmittedPayment { get; set; }
    public PaymentConfirmationProof? Proof { get; set; }
}