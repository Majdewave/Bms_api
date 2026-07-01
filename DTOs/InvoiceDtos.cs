namespace Clienta.Api.DTOs;

public sealed class CreateInvoiceRequest
{
    public Guid ClientId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Notes { get; set; }
    public decimal? VatRate { get; set; }
    public List<CreateInvoiceLineItemRequest> LineItems { get; set; } = new();
}

public sealed class CreateInvoiceLineItemRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
}