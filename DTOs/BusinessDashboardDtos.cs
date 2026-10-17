namespace Clienta.Api.DTOs;

public sealed record BusinessBillingContactDto(
    string Name,
    string? RoleTitle,
    string? Phone,
    string? WhatsApp,
    string? Email);

public sealed record BusinessNextBillingDto(
    Guid ProjectId,
    string ProjectName,
    Guid ClientId,
    string ClientName,
    Guid MilestoneId,
    string MilestoneTitle,
    decimal Amount,
    DateTime DueDate,
    bool IsOverdue,
    BusinessBillingContactDto? BillingContact);

// ProjectValue and NotYetInvoiced are before-VAT plan figures; Invoiced/Paid/Outstanding use invoice payable amounts (existing payment semantics).
// Invoiced/Paid/Outstanding are null when the caller lacks invoice access.
public sealed record BusinessDashboardResponse(
    string Currency,
    int ProjectCount,
    decimal ProjectValue,
    bool InvoiceFinancialsAvailable,
    decimal? Invoiced,
    decimal? Paid,
    decimal? Outstanding,
    decimal NotYetInvoiced,
    BusinessNextBillingDto? NextBilling);
