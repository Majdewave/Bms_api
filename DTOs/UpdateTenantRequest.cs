namespace Clienta.Api.DTOs;

public class UpdateTenantRequest
{
    public string? Name { get; set; }
    public string? LogoUrl { get; set; }
    public string? BusinessStampUrl { get; set; }
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public decimal? DefaultVatRate { get; set; }
    public string? Currency { get; set; }
    public string? InvoicePrefix { get; set; }
    public int? NextInvoiceNumber { get; set; }
}
