using Clienta.Api.Models;

namespace Clienta.Api.Entities;

public sealed class CommercialPlanConfiguration
{
    public Guid Id { get; set; }
    public TenantProductType ProductType { get; set; }
    public PlanType PlanType { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }
    public bool Enabled { get; set; }
    public bool AvailableForPurchase { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}