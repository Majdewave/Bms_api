using Microsoft.AspNetCore.Http;

namespace Clienta.Api.DTOs;

public sealed record CreatePaymentConfirmationRequest(
    decimal? RequestedAmount,
    int? ExpiresInDays);

public sealed record PaymentConfirmationCreatedResponse(
    Guid Id,
    string PublicToken,
    string PublicUrl,
    decimal? RequestedAmount,
    string CurrencyCode,
    DateTime ExpiresAt);

public sealed record PublicPaymentConfirmationResponse(
    string BusinessName,
    string? BusinessLogoUrl,
    string InvoiceNumber,
    string? ProjectName,
    IReadOnlyList<string> PaymentDescriptions,
    decimal? RequestedAmount,
    decimal CurrentRemaining,
    string CurrencyCode,
    DateTime ExpiresAt,
    string RequestState,
    string? SubmissionState,
    string DefaultLanguage);

public sealed record PublicPaymentConfirmationSubmissionResponse(
    decimal SubmittedAmount,
    DateTime SubmittedAt,
    string State);

public sealed class SubmitPaymentConfirmationRequest
{
    public decimal AmountPaid { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? Reference { get; set; }
    public IFormFile? ProofFile { get; set; }
}

public sealed record PaymentConfirmationDetailsResponse(
    Guid Id,
    Guid InvoiceId,
    string InvoiceNumber,
    decimal? RequestedAmount,
    decimal? SubmittedAmount,
    string CurrencyCode,
    DateTime ExpiresAt,
    string Status,
    DateTime? SubmittedAt,
    DateTime? RejectedAt,
    string? RejectionReason,
    bool HasProof,
    string? SubmittedPaymentStatus);

public sealed record PaymentConfirmationReviewResponse(
    Guid Id,
    Guid? SubmittedPaymentId,
    Guid InvoiceId,
    string InvoiceNumber,
    string ClientName,
    string? ProjectName,
    decimal? RequestedAmount,
    decimal? SubmittedAmount,
    string CurrencyCode,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    string Status,
    DateTime? SubmittedAt,
    DateTime? RejectedAt,
    string? RejectionReason,
    bool HasProof,
    string? SubmittedPaymentStatus);

public sealed record RejectPaymentConfirmationRequest(string? Reason);