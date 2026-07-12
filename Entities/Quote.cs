using Clienta.Api.Models;

namespace Clienta.Api.Entities;

public class Quote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Guid? ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public DateTime QuoteDate { get; set; }
    public DateTime? ValidUntil { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatRate { get; set; } = 18m;
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public string? BusinessName { get; set; }
    public string? BusinessAddress { get; set; }
    public string? BusinessPhone { get; set; }
    public string? BusinessEmail { get; set; }
    public string? LogoUrl { get; set; }
    public string? LogoBase64 { get; set; }
    public string Language { get; set; } = "en";
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<QuoteLineItem> LineItems { get; set; } = new();
}
