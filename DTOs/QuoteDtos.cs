namespace Clienta.Api.DTOs;

public sealed class CreateQuoteRequest
{
    public bool IsExistingClient { get; set; } = true;
    public Guid? ClientId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public DateTime QuoteDate { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? Notes { get; set; }
    public decimal? VatRate { get; set; }
    public string? Status { get; set; }
    public List<CreateQuoteLineItemRequest> LineItems { get; set; } = new();
}

public sealed class CreateQuoteLineItemRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class QuoteResponse
{
    public Guid Id { get; set; }
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
    public decimal VatRate { get; set; }
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
    public string Status { get; set; } = "draft";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<QuoteLineItemResponse> LineItems { get; set; } = new();
}

public sealed class QuoteLineItemResponse
{
    public Guid Id { get; set; }
    public Guid QuoteId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
