namespace Clienta.Api.DTOs;

public sealed class CreateInvoiceRequest
{
    public Guid ClientId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? AllocationNumber { get; set; }
    public string? Notes { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? WithholdingTaxRate { get; set; }
    public string? PaymentMethod { get; set; }
    public int? Installments { get; set; }
    public string? Status { get; set; }
    public List<CreateInvoiceLineItemRequest> LineItems { get; set; } = new();
}

public sealed class CreateInvoiceLineItemRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
}

public sealed class InvoiceResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal VatRate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal WithholdingTaxRate { get; set; }
    public decimal WithholdingTaxAmount { get; set; }
    public decimal FinalAmountToPay { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public int? Installments { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? AllocationNumber { get; set; }
    public string? Notes { get; set; }
    public string? BusinessName { get; set; }
    public string? LegalBusinessName { get; set; }
    public string? BusinessRegistrationNumber { get; set; }
    public string? BusinessAddress { get; set; }
    public string? BusinessPhone { get; set; }
    public string? BusinessEmail { get; set; }
    public string? LogoUrl { get; set; }
    public string? BusinessStampUrl { get; set; }
    public string? LogoBase64 { get; set; }
    public string Language { get; set; } = "en";
    public DateTime CreatedAt { get; set; }
    public List<InvoiceLineItemResponse> LineItems { get; set; } = new();
}

public sealed class InvoiceLineItemResponse
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Total { get; set; }
}