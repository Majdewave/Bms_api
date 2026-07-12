using System.ComponentModel.DataAnnotations.Schema;

namespace Clienta.Api.Entities;

public class QuoteLineItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuoteId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    [NotMapped]
    public decimal Total => Quantity * UnitPrice;
}
