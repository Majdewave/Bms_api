using Clienta.Api.Models;

namespace Clienta.Api.Entities;

public class BillingMilestone : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime? DueDate { get; set; }
    public BillingMilestoneStatus Status { get; set; } = BillingMilestoneStatus.Planned;
    public Guid? InvoiceId { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project Project { get; set; } = default!;
    public Invoice? Invoice { get; set; }
}
