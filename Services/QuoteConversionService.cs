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

        var isBusinessTenant = tenant.ProductType == TenantProductType.Business;

        return new Invoice
        {
            TenantId = tenant.Id,
            InvoiceNumber = invoiceNumber,
            ClientId = quote.ClientId.Value,
            ProjectId = quote.ProjectId,
            ClientName = quote.ClientName,
            InvoiceDate = DateTime.UtcNow,
            DueDate = quote.ValidUntil,
            Subtotal = quote.Subtotal,
            VatRate = quote.VatRate,
            VatAmount = quote.VatAmount,
            TotalAmount = quote.TotalAmount,
            Amount = quote.TotalAmount,
            Notes = quote.Notes,
            BusinessName = isBusinessTenant ? tenant.Name : quote.BusinessName,
            BusinessAddress = isBusinessTenant ? tenant.Address : quote.BusinessAddress,
            LegalBusinessName = isBusinessTenant ? tenant.LegalBusinessName : null,
            BusinessRegistrationNumber = isBusinessTenant ? tenant.BusinessRegistrationNumber : null,
            BusinessPhone = isBusinessTenant ? tenant.Phone : quote.BusinessPhone,
            BusinessEmail = isBusinessTenant ? tenant.OwnerUser?.Email : quote.BusinessEmail,
            BusinessStampUrl = isBusinessTenant ? tenant.BusinessStampUrl : null,
            LogoUrl = isBusinessTenant ? tenant.LogoUrl : quote.LogoUrl,
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
