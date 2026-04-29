using System.ComponentModel.DataAnnotations.Schema;

namespace Clienta.Api.Entities;

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = "";
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Notes { get; set; }
    public string? BusinessName { get; set; }
    public string? BusinessAddress { get; set; }
    public string? BusinessPhone { get; set; }
    public string? BusinessEmail { get; set; }
    public string? LogoUrl { get; set; }
    public string? LogoBase64 { get; set; }
    public string Language { get; set; } = "en";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<InvoiceLineItem> LineItems { get; set; } = new();
}