using Clienta.Api.Models;

namespace Clienta.Api.DTOs;

public sealed record CreateBillingMilestoneRequest(
    string Title,
    decimal Amount,
    DateTime? DueDate,
    string? Description,
    BillingMilestoneStatus Status = BillingMilestoneStatus.Planned,
    int SortOrder = 0);

public sealed record UpdateBillingMilestoneRequest(
    string Title,
    decimal Amount,
    DateTime? DueDate,
    string? Description,
    BillingMilestoneStatus Status,
    int SortOrder = 0);

public sealed record BillingMilestoneResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    decimal Amount,
    DateTime? DueDate,
    BillingMilestoneStatus Status,
    Guid? InvoiceId,
    string? InvoiceNumber,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int SortOrder);
