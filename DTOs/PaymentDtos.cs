using Clienta.Api.Models;

namespace Clienta.Api.DTOs;

public sealed record CreatePaymentRequest(
    decimal Amount,
    DateTime PaymentDate,
    string PaymentMethod,
    string? Reference,
    string? Notes);

public sealed record UpdatePaymentRequest(
    decimal Amount,
    DateTime PaymentDate,
    string PaymentMethod,
    string? Reference,
    string? Notes);

public sealed record PaymentResponse(
    Guid Id,
    Guid InvoiceId,
    Guid ClientId,
    Guid? ProjectId,
    decimal Amount,
    DateTime PaymentDate,
    string PaymentMethod,
    string? Reference,
    string? Notes,
    PaymentStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? CreatedByUserId,
    Guid? VerifiedByUserId,
    DateTime? VerifiedAt,
    string? CreatedByName,
    string? CreatedByEmail);

public sealed record InvoicePaymentSummaryResponse(
    decimal InvoiceTotalPayable,
    decimal VerifiedPaid,
    decimal RemainingBalance,
    string PaymentState,
    IReadOnlyList<PaymentResponse> Payments);

public sealed record PaymentTotalsResponse(
    decimal InvoiceAmountDue,
    decimal Paid,
    decimal Outstanding);
