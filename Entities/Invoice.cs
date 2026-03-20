namespace Clienta.Api.Entities;

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = "";
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}