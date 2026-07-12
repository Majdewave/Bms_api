using Clienta.Api.Entities;
using Clienta.Api.Models;

namespace Clienta.Api.Services;

public interface IQuoteConversionService
{
    Invoice BuildInvoiceFromQuote(Quote quote, Tenant tenant, string invoiceNumber);
}

public class QuoteConversionService : IQuoteConversionService
{
    public Invoice BuildInvoiceFromQuote(Quote quote, Tenant tenant, string invoiceNumber)
    {
        if (!quote.ClientId.HasValue)
        {
            throw new InvalidOperationException("Cannot convert a quote without an existing client to an invoice.");
        }

        return new Invoice
        {
            TenantId = tenant.Id,
            InvoiceNumber = invoiceNumber,
            ClientId = quote.ClientId.Value,
            ClientName = quote.ClientName,
            InvoiceDate = DateTime.UtcNow,
            DueDate = quote.ValidUntil,
            Subtotal = quote.Subtotal,
            VatRate = quote.VatRate,
            VatAmount = quote.VatAmount,
            TotalAmount = quote.TotalAmount,
            Amount = quote.TotalAmount,
            Notes = quote.Notes,
            BusinessName = quote.BusinessName,
            BusinessAddress = quote.BusinessAddress,
            BusinessPhone = quote.BusinessPhone,
            BusinessEmail = quote.BusinessEmail,
            LogoUrl = quote.LogoUrl,
            LogoBase64 = quote.LogoBase64,
            Language = quote.Language,
            Status = InvoiceStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            LineItems = quote.LineItems.Select(li => new InvoiceLineItem
            {
                Description = li.Description,
                Quantity = li.Quantity,
                Price = li.UnitPrice,
            }).ToList(),
        };
    }
}
