using System.ComponentModel.DataAnnotations.Schema;

namespace Clienta.Api.Entities;

public class InvoiceLineItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal Price { get; set; }

    [NotMapped]
    public decimal Total => Quantity * Price;
}