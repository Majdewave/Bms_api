using System.Globalization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize(Policy = "manage_invoices")]
public class InvoicesController : ControllerBase
{
    private const decimal DefaultVatRate = 18m;

    private readonly AppDbContext _db;
    private readonly IFeatureService _featureService;
    private readonly ITenantContext _tenantContext;
    private readonly IWebHostEnvironment _env;
    public InvoicesController(AppDbContext db, ITenantContext tenantContext, IFeatureService featureService, IWebHostEnvironment env)
    {
        _db = db;
        _tenantContext = tenantContext;
        _featureService = featureService;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!await _featureService.IsEnabledAsync("invoices"))
            return Forbid();

        var invoices = await _db.Invoices
            .Include(invoice => invoice.LineItems)
            .OrderByDescending(invoice => invoice.CreatedAt)
            .ToListAsync();

        return Ok(invoices.Select(MapInvoiceResponse));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        if (!await _featureService.IsEnabledAsync("invoices"))
            return Forbid();

        var invoice = await _db.Invoices
            .Include(item => item.LineItems)
            .FirstOrDefaultAsync(item => item.Id == id);

        Console.WriteLine($"Invoice has {invoice.LineItems.Count} line items");

        foreach (var item in invoice.LineItems)
        {
            Console.WriteLine($"DB LineItem: {item.Id}");
        }

        if (invoice == null)
            return NotFound();

        return Ok(MapInvoiceResponse(invoice));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest request)
    {
        if (!await _featureService.IsEnabledAsync("invoices"))
            return Forbid();

        var client = await _db.Clients.FindAsync(request.ClientId);
        if (client == null)
            return BadRequest("Client not found");

        if (!ValidateInvoiceRequestSettings(request, out var invoiceSettingsValidationError))
            return BadRequest(invoiceSettingsValidationError);

        if (!TryBuildLineItems(request.LineItems, out var lineItems, out var validationError))
            return BadRequest(validationError);

        var tenant = await GetTenantForWriteAsync();
        if (tenant == null)
            return NotFound("Tenant not found");

        var invoiceNumber = NormalizeInvoiceNumber(tenant);
        if (await InvoiceNumberExistsAsync(tenant.Id, invoiceNumber))
            return Conflict("Invoice number already exists.");

        var invoice = new Invoice();
        invoice.LineItems.AddRange(lineItems);

        invoice.TenantId = tenant.Id;
        invoice.InvoiceNumber = invoiceNumber;

        ApplyInvoiceValues(invoice, request, client, tenant, invoice.LineItems.ToList(), applyTenantSnapshot: true);


        try
        {
            _db.Invoices.Add(invoice);

            tenant.NextInvoiceNumber = tenant.NextInvoiceNumber > 0
                ? tenant.NextInvoiceNumber + 1
                : 2;

            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Invoice number already exists.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                ex.Message,
                ex.StackTrace
            });
        }

        return Ok(MapInvoiceResponse(invoice));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateInvoiceRequest request)
    {
        if (!await _featureService.IsEnabledAsync("invoices"))
            return Forbid();

        var invoice = await _db.Invoices
            .Include(item => item.LineItems)
            .FirstOrDefaultAsync(item => item.Id == id);

        if (invoice == null)
            return NotFound();

        var client = await _db.Clients.FindAsync(request.ClientId);
        if (client == null)
            return BadRequest("Client not found");

        if (!ValidateInvoiceRequestSettings(request, out var invoiceSettingsValidationError))
            return BadRequest(invoiceSettingsValidationError);

        if (!TryBuildLineItems(request.LineItems, out var lineItems, out var validationError))
            return BadRequest(validationError);

        var tenant = await GetTenantForWriteAsync();
        if (tenant == null)
            return NotFound("Tenant not found");

        _db.InvoiceLineItems.RemoveRange(invoice.LineItems);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        // טען מחדש את החשבונית לאחר המחיקה
        invoice = await _db.Invoices
            .Include(i => i.LineItems)
            .FirstAsync(i => i.Id == id);

        ApplyInvoiceValues(invoice, request, client, tenant, lineItems, applyTenantSnapshot: false);

        // הוסף ישירות לטבלה ולא דרך ה-Navigation
        foreach (var lineItem in lineItems)
        {
            lineItem.Id = Guid.NewGuid();
            lineItem.InvoiceId = invoice.Id;

            _db.InvoiceLineItems.Add(lineItem);
        }
        try
        {
            await _db.SaveChangesAsync();
            return Ok(MapInvoiceResponse(invoice));
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entries = ex.Entries.Select(e => new
            {
                Entity = e.Entity.GetType().Name,
                State = e.State.ToString(),
                Keys = e.Properties
                    .Where(p => p.Metadata.IsPrimaryKey())
                    .ToDictionary(
                        p => p.Metadata.Name,
                        p => p.CurrentValue)
            });

            return StatusCode(500, new
            {
                ex.Message,
                Entries = entries
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                ex.Message,
                ex.StackTrace
            });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _featureService.IsEnabledAsync("invoices"))
            return Forbid();

        var invoice = await _db.Invoices.FindAsync(id);
        if (invoice == null)
            return NotFound();

        _db.Invoices.Remove(invoice);
        await _db.SaveChangesAsync();

        return Ok();
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id)
    {
        try { 
            QuestPDF.Drawing.FontManager.RegisterFont(
            System.IO.File.OpenRead(
                  Path.Combine(_env.WebRootPath, "fonts", "NotoSansHebrew-Regular.ttf")));

            if (!await _featureService.IsEnabledAsync("invoices"))
                return Forbid();

            var invoice = await _db.Invoices
                .Include(item => item.LineItems)
                .FirstOrDefaultAsync(item => item.Id == id);

            if (invoice == null)
                return NotFound();

            var tenant = await GetTenantAsync();
            var client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(item => item.Id == invoice.ClientId);
            var currencyCode = NormalizeCurrencyCode(tenant?.Currency);
            var labels = GetLabels(invoice.Language);
            var lineItems = BuildPdfLineItems(invoice, labels);
            var businessName = invoice.BusinessName ?? tenant?.Name ?? labels.BusinessFallback;
            var businessAddress = invoice.BusinessAddress;
            var businessPhone = invoice.BusinessPhone ?? tenant?.Phone;
            var businessWhatsApp = tenant?.WhatsApp;
            var businessEmail = invoice.BusinessEmail ?? tenant?.OwnerUser?.Email;
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var logoBytes = await TryLoadLogoBytesAsync(invoice.LogoBase64, invoice.LogoUrl ?? tenant?.LogoUrl, baseUrl);
            var businessStampBytes =
                await TryLoadBusinessStampBytesAsync(
                    invoice.BusinessStampUrl,
                    tenant?.BusinessStampUrl,
                    baseUrl); var generatedOn = DateTime.UtcNow;

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(text => text.FontSize(9));

                    page.Header().Element(header =>
                        ComposeHeader(
                            header,
                            labels,
                            labels.IsRtl,
                            logoBytes,
                            businessName,
                            invoice.LegalBusinessName,
                            invoice.BusinessRegistrationNumber,
                            businessAddress,
                            businessPhone,
                            businessWhatsApp,
                            businessEmail));

                    page.Content().PaddingVertical(8).Column(column =>
                    {
                        column.Spacing(10);
                        column.Item().Element(item => ComposeDetailsSection(item, labels, labels.IsRtl, invoice, client?.Phone));
                        column.Item().Element(item => ComposeLineItemsTable(item, labels, labels.IsRtl, lineItems, currencyCode));
                        column.Item().Element(item => ComposeTotalsSection(item, labels, labels.IsRtl, invoice, currencyCode));

                        if (!string.IsNullOrWhiteSpace(invoice.Notes))
                            column.Item().Element(item => ComposeNotesSection(item, labels, labels.IsRtl, invoice.Notes));

                        column.Item().Element(item => ComposeBusinessSignatureSection(item, labels, labels.IsRtl, businessStampBytes));

                    });

                    page.Footer().PaddingTop(6).Element(footer =>
                        ComposeFooter(footer, labels, labels.IsRtl, generatedOn));
                });
            }).GeneratePdf();

            return File(pdf, "application/pdf", $"invoice-{invoice.InvoiceNumber}.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.ToString());
        }
    }


    private void ApplyInvoiceValues(
        Invoice invoice,
        CreateInvoiceRequest request,
        Client client,
        Tenant? tenant,
        List<InvoiceLineItem> lineItems,
        bool applyTenantSnapshot)
    {
        var vatRate = NormalizeVatRate(request.VatRate, tenant);
        var paymentMethod = ParsePaymentMethod(request.PaymentMethod, tenant?.DefaultPaymentMethod ?? PaymentMethod.Cash);
        var installments = NormalizeInstallments(request.Installments, paymentMethod, tenant?.DefaultInstallments);
        var status = ParseInvoiceStatus(request.Status, tenant?.DefaultInvoiceStatus ?? InvoiceStatus.Pending);
        var withholdingTaxRate = NormalizeWithholdingTaxRate(request.WithholdingTaxRate, tenant);
        var subtotal = RoundMoney(lineItems.Sum(item => item.Quantity * item.Price));
        var vatAmount = RoundMoney(subtotal * vatRate / 100m);
        var totalAmount = RoundMoney(subtotal + vatAmount);
        var withholdingTaxAmount = RoundMoney(totalAmount * withholdingTaxRate / 100m);
        var finalAmountToPay = RoundMoney(totalAmount - withholdingTaxAmount);

        invoice.ClientId = client.Id;
        invoice.ClientName = client.FullName;
        invoice.InvoiceDate = request.InvoiceDate == default ? DateTime.UtcNow : request.InvoiceDate;
        invoice.DueDate = request.DueDate;
        invoice.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        invoice.VatRate = vatRate;
        invoice.Subtotal = subtotal;
        invoice.VatAmount = vatAmount;
        invoice.TotalAmount = totalAmount;
        invoice.WithholdingTaxRate = withholdingTaxRate;
        invoice.WithholdingTaxAmount = withholdingTaxAmount;
        invoice.FinalAmountToPay = finalAmountToPay;
        invoice.PaymentMethod = paymentMethod;
        invoice.Installments = installments;
        invoice.Status = status;
        invoice.Amount = totalAmount;

        if (applyTenantSnapshot)
        {
            invoice.BusinessName = tenant?.Name ?? invoice.BusinessName;
            invoice.LegalBusinessName = tenant?.LegalBusinessName;
            invoice.BusinessRegistrationNumber = tenant?.BusinessRegistrationNumber;
            invoice.BusinessPhone = tenant?.Phone ?? invoice.BusinessPhone;
            invoice.BusinessEmail = tenant?.OwnerUser?.Email ?? invoice.BusinessEmail;
            invoice.BusinessStampUrl = tenant?.BusinessStampUrl;
            invoice.LogoUrl = tenant?.LogoUrl ?? invoice.LogoUrl;
        }

        invoice.Language = ResolveLanguage();
    }

    private static bool TryBuildLineItems(
        List<CreateInvoiceLineItemRequest>? requestedLineItems,
        out List<InvoiceLineItem> lineItems,
        out string? validationError)
    {
        lineItems = new List<InvoiceLineItem>();
        validationError = null;

        if (requestedLineItems == null || requestedLineItems.Count == 0)
        {
            validationError = "At least one line item is required.";
            return false;
        }

        foreach (var requestedLineItem in requestedLineItems)
        {
            if (string.IsNullOrWhiteSpace(requestedLineItem.Description))
            {
                validationError = "Each line item must include a description.";
                return false;
            }

            if (requestedLineItem.Quantity <= 0)
            {
                validationError = "Each line item quantity must be greater than 0.";
                return false;
            }

            if (requestedLineItem.Price < 0)
            {
                validationError = "Each line item price must be 0 or higher.";
                return false;
            }

            lineItems.Add(new InvoiceLineItem
            {
                Description = requestedLineItem.Description.Trim(),
                Quantity = requestedLineItem.Quantity,
                Price = RoundMoney(requestedLineItem.Price),
            });
        }

        return true;
    }

    private static decimal NormalizeVatRate(decimal? requestedVatRate, Tenant? tenant)
    {
        if (requestedVatRate.HasValue)
        {
            if (requestedVatRate.Value >= 0 && requestedVatRate.Value <= 100)
                return RoundMoney(requestedVatRate.Value);

            return DefaultVatRate;
        }

        if (tenant is not null && tenant.DefaultVatRate >= 0 && tenant.DefaultVatRate <= 100)
            return RoundMoney(tenant.DefaultVatRate);

        return DefaultVatRate;
    }

    private static bool ValidateInvoiceRequestSettings(CreateInvoiceRequest request, out string? validationError)
    {
        validationError = null;

        if (request.VatRate.HasValue && (request.VatRate.Value < 0 || request.VatRate.Value > 100))
        {
            validationError = "VAT rate must be between 0 and 100.";
            return false;
        }

        if (
            request.WithholdingTaxRate.HasValue &&
            (request.WithholdingTaxRate.Value < 0 || request.WithholdingTaxRate.Value > 100)
        )
        {
            validationError = "Withholding tax rate must be between 0 and 100.";
            return false;
        }

        var requestedPaymentMethod = request.PaymentMethod?.Trim().ToLowerInvariant();
        if (requestedPaymentMethod == "credit" && request.Installments.HasValue)
        {
            if (request.Installments.Value < 1 || request.Installments.Value > 36)
            {
                validationError = "Installments must be between 1 and 36.";
                return false;
            }
        }

        return true;
    }

    private static decimal NormalizeWithholdingTaxRate(decimal? requestedWithholdingTaxRate, Tenant? tenant)
    {
        if (requestedWithholdingTaxRate.HasValue)
        {
            var requested = requestedWithholdingTaxRate.Value;
            if (requested >= 0 && requested <= 100)
                return RoundMoney(requested);
        }

        if (tenant is not null && tenant.DefaultWithholdingTaxRate >= 0 && tenant.DefaultWithholdingTaxRate <= 100)
            return RoundMoney(tenant.DefaultWithholdingTaxRate);

        return 0m;
    }

    private static PaymentMethod ParsePaymentMethod(string? value, PaymentMethod fallback)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "cash" => PaymentMethod.Cash,
            "credit" => PaymentMethod.Credit,
            "bank_transfer" => PaymentMethod.BankTransfer,
            "check" => PaymentMethod.Check,
            "bit" => PaymentMethod.Bit,
            "paybox" => PaymentMethod.PayBox,
            "other" => PaymentMethod.Other,
            _ => fallback
        };
    }

    private static InvoiceStatus ParseInvoiceStatus(string? value, InvoiceStatus fallback)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "pending" => InvoiceStatus.Pending,
            "paid" => InvoiceStatus.Paid,
            "partially_paid" or "partial" => InvoiceStatus.PartiallyPaid,
            "cancelled" or "canceled" => InvoiceStatus.Cancelled,
            _ => fallback
        };
    }

    private static int? NormalizeInstallments(int? requestedInstallments, PaymentMethod paymentMethod, int? fallbackInstallments)
    {
        if (paymentMethod != PaymentMethod.Credit)
            return null;

        var candidate = requestedInstallments ?? fallbackInstallments;
        if (!candidate.HasValue)
            return null;

        return candidate.Value >= 1 && candidate.Value <= 36 ? candidate.Value : null;
    }

    private static string ToApiPaymentMethod(PaymentMethod value)
    {
        return value switch
        {
            PaymentMethod.Cash => "cash",
            PaymentMethod.Credit => "credit",
            PaymentMethod.BankTransfer => "bank_transfer",
            PaymentMethod.Check => "check",
            PaymentMethod.Bit => "bit",
            PaymentMethod.PayBox => "paybox",
            PaymentMethod.Other => "other",
            _ => "cash"
        };
    }

    private static string ToApiInvoiceStatus(InvoiceStatus value)
    {
        return value switch
        {
            InvoiceStatus.Pending => "pending",
            InvoiceStatus.Paid => "paid",
            InvoiceStatus.PartiallyPaid => "partially_paid",
            InvoiceStatus.Cancelled => "cancelled",
            _ => "pending"
        };
    }

    private static InvoiceResponse MapInvoiceResponse(Invoice invoice)
    {
        return new InvoiceResponse
        {
            Id = invoice.Id,
            TenantId = invoice.TenantId,
            InvoiceNumber = invoice.InvoiceNumber,
            ClientId = invoice.ClientId,
            ClientName = invoice.ClientName,
            Amount = invoice.Amount,
            VatRate = invoice.VatRate,
            Subtotal = invoice.Subtotal,
            VatAmount = invoice.VatAmount,
            TotalAmount = invoice.TotalAmount,
            WithholdingTaxRate = invoice.WithholdingTaxRate,
            WithholdingTaxAmount = invoice.WithholdingTaxAmount,
            FinalAmountToPay = invoice.FinalAmountToPay,
            PaymentMethod = ToApiPaymentMethod(invoice.PaymentMethod),
            Installments = invoice.Installments,
            Status = ToApiInvoiceStatus(invoice.Status),
            InvoiceDate = invoice.InvoiceDate,
            DueDate = invoice.DueDate,
            Notes = invoice.Notes,
            BusinessName = invoice.BusinessName,
            LegalBusinessName = invoice.LegalBusinessName,
            BusinessRegistrationNumber = invoice.BusinessRegistrationNumber,
            BusinessAddress = invoice.BusinessAddress,
            BusinessPhone = invoice.BusinessPhone,
            BusinessEmail = invoice.BusinessEmail,
            LogoUrl = invoice.LogoUrl,
            BusinessStampUrl = invoice.BusinessStampUrl,
            LogoBase64 = invoice.LogoBase64,
            Language = invoice.Language,
            CreatedAt = invoice.CreatedAt,
            LineItems = invoice.LineItems
                .Select(lineItem => new InvoiceLineItemResponse
                {
                    Id = lineItem.Id,
                    InvoiceId = lineItem.InvoiceId,
                    Description = lineItem.Description,
                    Quantity = lineItem.Quantity,
                    Price = lineItem.Price,
                    Total = RoundMoney(lineItem.Total),
                })
                .ToList(),
        };
    }

    private string ResolveLanguage()
    {
        var requestedLanguage = Request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(requestedLanguage))
            return "en";

        var firstLanguage = requestedLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLanguage))
            return "en";

        return firstLanguage.ToLowerInvariant();
    }

    private List<InvoiceLineItem> BuildPdfLineItems(Invoice invoice, InvoicePdfLabels labels)
    {
        if (invoice.LineItems.Count > 0)
            return invoice.LineItems;

        return new List<InvoiceLineItem>
        {
            new()
            {
                Description = labels.DefaultLineItem,
                Quantity = 1,
                Price = invoice.Subtotal > 0 ? invoice.Subtotal : invoice.Amount,
            }
        };
    }

    private async Task<Tenant?> GetTenantAsync()
    {
        if (_tenantContext.TenantId != Guid.Empty)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Include(item => item.OwnerUser)
                .FirstOrDefaultAsync(item => item.Id == _tenantContext.TenantId);

            if (tenant != null)
                return tenant;
        }

        return await _db.Tenants
            .AsNoTracking()
            .Include(item => item.OwnerUser)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private async Task<Tenant?> GetTenantForWriteAsync()
    {
        if (_tenantContext.TenantId != Guid.Empty)
        {
            var tenant = await _db.Tenants
                .Include(item => item.OwnerUser)
                .FirstOrDefaultAsync(item => item.Id == _tenantContext.TenantId);

            if (tenant != null)
                return tenant;
        }

        return await _db.Tenants
            .Include(item => item.OwnerUser)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private async Task<bool> InvoiceNumberExistsAsync(Guid tenantId, string invoiceNumber, Guid? excludeInvoiceId = null)
    {
        var query = _db.Invoices
            .IgnoreQueryFilters()
            .Where(invoice => invoice.TenantId == tenantId && invoice.InvoiceNumber == invoiceNumber);

        if (excludeInvoiceId.HasValue)
            query = query.Where(invoice => invoice.Id != excludeInvoiceId.Value);

        return await query.AnyAsync();
    }

    private static string NormalizeInvoiceNumber(Tenant tenant)
    {
        var prefix = string.IsNullOrWhiteSpace(tenant.InvoicePrefix)
            ? "INV-"
            : tenant.InvoicePrefix.Trim();

        var nextNumber = tenant.NextInvoiceNumber > 0
            ? tenant.NextInvoiceNumber
            : 1;

        return $"{prefix}{nextNumber}";
    }

    private static async Task<byte[]?> TryLoadBusinessStampBytesAsync(
        string? invoiceStampUrl,
        string? tenantStampUrl,
        string baseUrl)
    {
        return await TryLoadAssetBytesAsync(
            invoiceStampUrl ?? tenantStampUrl,
            baseUrl);
    }

    private static async Task<byte[]?> TryLoadLogoBytesAsync(string? logoBase64, string? logoUrl, string baseUrl)
    {
        var base64Bytes = TryDecodeBase64(logoBase64);
        if (base64Bytes != null)
            return base64Bytes;

        return await TryLoadAssetBytesAsync(logoUrl, baseUrl);
    }

    private static async Task<byte[]?> TryLoadAssetBytesAsync(string? assetUrl, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(assetUrl) || !TryBuildAssetUri(assetUrl, baseUrl, out var uri))
            return null;

        try
        {
            using var client = new HttpClient();
            return await client.GetByteArrayAsync(uri);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryBuildAssetUri(string assetUrl, string baseUrl, out Uri uri)
    {
        if (Uri.TryCreate(assetUrl, UriKind.Absolute, out uri))
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var absoluteBase) &&
            Uri.TryCreate(absoluteBase, assetUrl, out uri))
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

        uri = default!;
        return false;
    }

    private static TextStyle HebrewStyle()
    {
        return TextStyle.Default.FontFamily("Noto Sans Hebrew").DirectionFromRightToLeft();
    }


    private static byte[]? TryDecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        var commaIndex = normalized.IndexOf(',');

        if (normalized.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
            normalized = normalized[(commaIndex + 1)..];

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch
        {
            return null;
        }
    }

    private static void ComposeHeader(
        IContainer container,
        InvoicePdfLabels labels,
        bool isRtl,
        byte[]? logoBytes,
        string businessName,
        string? legalBusinessName,
        string? businessRegistrationNumber,
        string? businessAddress,
        string? businessPhone,
        string? businessWhatsApp,
        string? businessEmail)
    {
        var leftTitle = labels.InvoiceTitle == "חשבונית מס" ? "חשבונית מס / קבלה" : labels.InvoiceTitle;
        var phoneLabel = isRtl ? "טלפון:" : labels.PhoneLabel + ":";

        container
            .PaddingBottom(2)
            .Column(column =>
            {
                column.Spacing(2);

                column.Item().MinHeight(90).Row(row =>
                {
                    row.Spacing(16);

                    row.ConstantItem(190)
                        .AlignMiddle()
                        .Column(leftColumn =>
                        {
                            leftColumn.Spacing(1);

                            var phoneLabelText = leftColumn.Item()
                                .AlignLeft()
                                .Text(phoneLabel)
                                .FontSize(9)
                                .SemiBold()
                                .FontColor(Colors.Grey.Darken1);

                            if (isRtl)
                                phoneLabelText.Style(HebrewStyle());

                            if (!string.IsNullOrWhiteSpace(businessPhone))
                            {
                                var phone = leftColumn.Item()
                                    .AlignLeft()
                                    .Text(businessPhone.Trim())
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken1);

                                if (isRtl)
                                    phone.Style(HebrewStyle());
                            }

                            var whatsappLabelText = leftColumn.Item()
                                .PaddingTop(2)
                                .AlignLeft()
                                .Text("WhatsApp:")
                                .FontSize(9)
                                .SemiBold()
                                .FontColor(Colors.Grey.Darken1);

                            if (isRtl)
                                whatsappLabelText.Style(HebrewStyle());

                            if (!string.IsNullOrWhiteSpace(businessWhatsApp))
                            {
                                var whatsapp = leftColumn.Item()
                                    .AlignLeft()
                                    .Text(businessWhatsApp.Trim())
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken1);

                                if (isRtl)
                                    whatsapp.Style(HebrewStyle());
                            }
                        });

                    row.ConstantItem(110)
                        .AlignMiddle()
                        .AlignCenter()
                        .Element(center =>
                        {
                            if (logoBytes != null)
                            {
                                center.Height(96)
                                    .Image(logoBytes)
                                    .FitArea();
                            }
                            else
                            {
                                center.Height(96);
                            }
                        });

                    row.RelativeItem()
                        .AlignMiddle()
                        .Column(rightColumn =>
                        {
                            rightColumn.Spacing(1);

                            var businessNameText = rightColumn.Item()
                                .AlignRight()
                                .Text(businessName)
                                .FontSize(13)
                                .SemiBold();

                            if (isRtl)
                                businessNameText.Style(HebrewStyle());

                            if (!string.IsNullOrWhiteSpace(legalBusinessName))
                            {
                                var legalNameText = rightColumn.Item()
                                    .AlignRight()
                                    .Text(legalBusinessName.Trim())
                                    .FontSize(14)
                                    .SemiBold()
                                    .FontColor(Colors.Grey.Darken3);

                                if (isRtl)
                                    legalNameText.Style(HebrewStyle());
                            }

                            if (!string.IsNullOrWhiteSpace(businessRegistrationNumber))
                            {
                                var registrationText = rightColumn.Item()
                                    .AlignRight()
                                    .Text($"ח.פ. {businessRegistrationNumber.Trim()}")
                                    .FontSize(12)
                                    .FontColor(Colors.Grey.Medium);

                                if (isRtl)
                                    registrationText.Style(HebrewStyle());
                            }
                        });
                });

                var title = column.Item()
                    .PaddingTop(2)
                    .AlignCenter()
                    .Text(leftTitle)
                    .FontSize(20)
                    .Bold()
                    .FontColor(Colors.Blue.Darken2);

                if (isRtl)
                    title.Style(HebrewStyle());
            });
    }

    private static void ComposeDetailsSection(IContainer container, InvoicePdfLabels labels, bool isRtl, Invoice invoice, string? clientPhone)
    {
        container.Row(row =>
        {
            row.Spacing(14);

            if (isRtl)
            {
                row.RelativeItem().Element(item => ComposeMetaCard(item, content => ComposeInvoiceMeta(content, labels, true, invoice)));
                row.RelativeItem().Element(item => ComposeMetaCard(item, content => ComposeClientMeta(content, labels, true, invoice, clientPhone)));
            }
            else
            {
                row.RelativeItem().Element(item => ComposeMetaCard(item, content => ComposeClientMeta(content, labels, false, invoice, clientPhone)));
                row.RelativeItem().Element(item => ComposeMetaCard(item, content => ComposeInvoiceMeta(content, labels, false, invoice)));
            }
        });
    }

    private static void ComposeMetaCard(IContainer container, Action<IContainer> content)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .Background(Colors.White)
            .MinHeight(82)
            .Padding(10)
            .Element(item => content(item));
    }

    private static void ComposeClientMeta(
        IContainer container,
        InvoicePdfLabels labels,
        bool isRtl,
        Invoice invoice,
        string? clientPhone)
    {
        AlignForDirection(container, isRtl).Column(column =>
        {
            column.Spacing(4);

            var title = column.Item()
                .Text(labels.ClientSectionTitle)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2)
                .FontSize(12);

            if (isRtl)
                title.Style(HebrewStyle());

            void AddField(string label, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var labelText = column.Item().AlignRight().Text(label).Bold().FontFamily("Noto Sans Hebrew");
                var valueText = column.Item().AlignRight().Text(value).FontFamily("Noto Sans Hebrew");

                if (isRtl)
                    valueText.Style(HebrewStyle());

                column.Item().PaddingBottom(2);
            }

            AddField(labels.ClientNameLabel, invoice.ClientName);
            AddField(labels.PhoneLabel, clientPhone);
        });
    }

    private static void ComposeInvoiceMeta(
        IContainer container,
        InvoicePdfLabels labels,
        bool isRtl,
        Invoice invoice)
    {
        AlignForDirection(container, isRtl).Column(column =>
        {
            column.Spacing(4);

            var title = column.Item()
                .Text(labels.InvoiceSectionTitle)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2)
                .FontSize(12);

            if (isRtl)
                title.Style(HebrewStyle());

            void AddField(string label, string value)
            {
                var labelText = column.Item().AlignRight().Text(label).Bold().FontFamily("Noto Sans Hebrew");
                var valueText = column.Item().AlignRight().Text(value).FontFamily("Noto Sans Hebrew");

                if (isRtl)
                    valueText.Style(HebrewStyle());

                column.Item().PaddingBottom(2);
            }

            AddField(labels.InvoiceNumberLabel, invoice.InvoiceNumber);
            AddField(labels.InvoiceDateLabel, FormatDate(invoice.InvoiceDate));
            AddField(labels.DueDateLabel, FormatDate(invoice.DueDate));
        });
    }

    private static void ComposeLineItemsTable(IContainer container, InvoicePdfLabels labels, bool isRtl, List<InvoiceLineItem> lineItems, string currencyCode)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(70);
                columns.ConstantColumn(90);
                columns.ConstantColumn(90);
            });

            table.Header(header =>
            {
                if (isRtl)
                {
                    AddHeaderCell(header, labels.QuantityLabel);
                    AddHeaderCell(header, labels.PriceLabel);
                    AddHeaderCell(header, labels.TotalLabel);
                    AddHeaderCell(header, labels.DescriptionLabel);
                    
                   
                    
                }
                else
                {
                    header.Cell().Element(TableHeaderStyle).AlignRight().Text(labels.QuantityLabel);
                    header.Cell().Element(TableHeaderStyle).AlignRight().Text(labels.PriceLabel);
                    header.Cell().Element(TableHeaderStyle).AlignRight().Text(labels.TotalLabel);
                    header.Cell().Element(TableHeaderStyle).Text(labels.DescriptionLabel);
                }
            });

            foreach (var lineItem in lineItems)
            {
                if (isRtl)
                {
                    table.Cell().Element(TableCellStyle).AlignRight().Text(FormatMoney(lineItem.Total, currencyCode, true));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(FormatMoney(lineItem.Price, currencyCode, true));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(lineItem.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(text =>{text.Span(lineItem.Description).Style(HebrewStyle()); });
                }
                else
                {
                    table.Cell().Element(TableCellStyle).Text(lineItem.Description);
                    table.Cell().Element(TableCellStyle).AlignRight().Text(lineItem.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(FormatMoney(lineItem.Price, currencyCode, false));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(FormatMoney(lineItem.Total, currencyCode, false));
                }
            }
        });
    }

    private static void ComposeTotalsSection(IContainer container, InvoicePdfLabels labels, bool isRtl, Invoice invoice, string currencyCode)
    {
        container.AlignRight().Width(260).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(column =>
        {
            column.Spacing(6);
            column.Item().Element(item => ComposeTotalRow(item, labels.SubtotalLabel, FormatMoney(invoice.Subtotal, currencyCode, isRtl), isRtl, false));
            column.Item().Element(item => ComposeTotalRow(item, $"{labels.VatLabel} ({invoice.VatRate:0.##}%)", FormatMoney(invoice.VatAmount, currencyCode, isRtl), isRtl, false));
            column.Item().Element(item => ComposeTotalRow(item, $"{labels.WithholdingTaxLabel} ({invoice.WithholdingTaxRate:0.##}%)", FormatMoney(invoice.WithholdingTaxAmount, currencyCode, isRtl), isRtl, false));
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            column.Item().Element(item => ComposeTotalRow(item, labels.TotalSectionLabel, FormatMoney(invoice.TotalAmount, currencyCode, isRtl), isRtl, false));
            column.Item().Element(item => ComposeTotalRow(item, labels.FinalAmountLabel, FormatMoney(invoice.FinalAmountToPay, currencyCode, isRtl), isRtl, true));
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            column.Item().Element(item => ComposeTotalRow(item, labels.PaymentMethodLabel, GetPaymentMethodDisplay(invoice.PaymentMethod, invoice.Language), isRtl, false));
            if (invoice.Installments.HasValue)
                column.Item().Element(item => ComposeTotalRow(item, labels.InstallmentsLabel, invoice.Installments.Value.ToString(CultureInfo.InvariantCulture), isRtl, false));
            column.Item().Element(item => ComposeTotalRow(item, labels.StatusLabel, GetInvoiceStatusDisplay(invoice.Status, invoice.Language), isRtl, false));
        });
    }

    private static string GetPaymentMethodDisplay(PaymentMethod paymentMethod, string? language)
    {
        var code = (language ?? "en").Trim().ToLowerInvariant();

        return code switch
        {
            "he" or "he-il" => paymentMethod switch
            {
                PaymentMethod.Cash => "מזומן",
                PaymentMethod.Credit => "אשראי",
                PaymentMethod.BankTransfer => "העברה בנקאית",
                PaymentMethod.Check => "צ'ק",
                PaymentMethod.Bit => "BIT",
                PaymentMethod.PayBox => "PayBox",
                PaymentMethod.Other => "אחר",
                _ => "מזומן"
            },
            "ar" or "ar-sa" or "ar-eg" => paymentMethod switch
            {
                PaymentMethod.Cash => "نقداً",
                PaymentMethod.Credit => "بطاقة ائتمان",
                PaymentMethod.BankTransfer => "تحويل بنكي",
                PaymentMethod.Check => "شيك",
                PaymentMethod.Bit => "BIT",
                PaymentMethod.PayBox => "PayBox",
                PaymentMethod.Other => "أخرى",
                _ => "نقداً"
            },
            _ => paymentMethod switch
            {
                PaymentMethod.Cash => "Cash",
                PaymentMethod.Credit => "Credit",
                PaymentMethod.BankTransfer => "Bank Transfer",
                PaymentMethod.Check => "Check",
                PaymentMethod.Bit => "BIT",
                PaymentMethod.PayBox => "PayBox",
                PaymentMethod.Other => "Other",
                _ => "Cash"
            }
        };
    }

    private static string GetInvoiceStatusDisplay(InvoiceStatus status, string? language)
    {
        var code = (language ?? "en").Trim().ToLowerInvariant();

        return code switch
        {
            "he" or "he-il" => status switch
            {
                InvoiceStatus.Pending => "ממתין לתשלום",
                InvoiceStatus.Paid => "שולם",
                InvoiceStatus.PartiallyPaid => "שולם חלקית",
                InvoiceStatus.Cancelled => "בוטל",
                _ => "ממתין לתשלום"
            },
            "ar" or "ar-sa" or "ar-eg" => status switch
            {
                InvoiceStatus.Pending => "بانتظار الدفع",
                InvoiceStatus.Paid => "مدفوع",
                InvoiceStatus.PartiallyPaid => "مدفوع جزئياً",
                InvoiceStatus.Cancelled => "ملغاة",
                _ => "بانتظار الدفع"
            },
            _ => status switch
            {
                InvoiceStatus.Pending => "Pending",
                InvoiceStatus.Paid => "Paid",
                InvoiceStatus.PartiallyPaid => "Partially Paid",
                InvoiceStatus.Cancelled => "Cancelled",
                _ => "Pending"
            }
        };
    }
    private static void ComposeBusinessSignatureSection(IContainer container, InvoicePdfLabels labels, bool isRtl, byte[]? stampBytes)
    {
        container.PaddingTop(4).AlignCenter().Column(column =>
        {
            var title = column.Item().AlignCenter().Text(labels.BusinessSignatureLabel).SemiBold().FontColor(Colors.Blue.Darken2).FontSize(11);
            if (isRtl)
                title.Style(HebrewStyle());

            column.Item().PaddingTop(4);

            if (stampBytes != null && stampBytes.Length > 0)
            {
                column.Item().AlignCenter().Width(120).Height(40).Image(stampBytes).FitArea();
            }
            else
            {
                column.Item().AlignCenter().Width(240).LineHorizontal(1).LineColor(Colors.Grey.Medium);
            }
        });
    }

    private static void ComposeTotalRow(
        IContainer container,
        string label,
        string value,
        bool isRtl,
        bool emphasize)
    {
        container.Row(row =>
        {
            if (isRtl)
            {
                var valueText = row.RelativeItem()
                    .AlignRight()
                    .Text(value);

                if (emphasize)
                    valueText.SemiBold().FontSize(12);

                valueText.Style(HebrewStyle());

                var labelText = row.RelativeItem()
                    .AlignRight()
                    .Text(label);

                if (emphasize)
                    labelText.SemiBold().FontSize(12);

                labelText.Style(HebrewStyle());
            }
            else
            {
                var labelText = row.RelativeItem().Text(label);

                if (emphasize)
                    labelText.SemiBold().FontSize(12);

                var valueText = row.RelativeItem()
                    .AlignRight()
                    .Text(value);

                if (emphasize)
                    valueText.SemiBold().FontSize(12);
            }
        });
    }

    private static void ComposeNotesSection(
        IContainer container,
        InvoicePdfLabels labels,
        bool isRtl,
        string notes)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .Background(Colors.White)
            .Padding(10)
            .MinHeight(55)
            .Element(item => AlignForDirection(item, isRtl))
            .Column(column =>
            {
                column.Spacing(3);

                var title = column.Item()
                    .AlignRight()
                    .Text(labels.NotesLabel)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken2);

                if (isRtl)
                    title.Style(HebrewStyle());

                var body = column.Item()
                    .AlignRight()
                    .Text(notes);

                if (isRtl)
                    body.Style(HebrewStyle());
            });
    }



    private static void ComposeFooter(IContainer container, InvoicePdfLabels labels, bool isRtl, DateTime generatedOn)
    {
        container.Row(row =>
        {
            if (isRtl)
            {
                var generated = row.RelativeItem().AlignRight().Text($"{labels.GeneratedOnLabel}: {generatedOn:dd/MM/yyyy HH:mm}").FontSize(9).FontColor(Colors.Grey.Darken1);
                generated.Style(HebrewStyle());

                row.RelativeItem().AlignCenter().Text(labels.PoweredByLabel).FontSize(9).FontColor(Colors.Grey.Darken1);
                row.RelativeItem().AlignLeft().Text(text =>
                {
                    text.Span(labels.PageLabel + " ").Style(HebrewStyle()).FontSize(9).FontColor(Colors.Grey.Darken1);
                    text.CurrentPageNumber().Style(HebrewStyle()).FontSize(9).FontColor(Colors.Grey.Darken1);
                    text.Span($" {labels.OfLabel} ").Style(HebrewStyle()).FontSize(9).FontColor(Colors.Grey.Darken1);
                    text.TotalPages().Style(HebrewStyle()).FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            }
            else
            {
                row.RelativeItem().Text($"{labels.GeneratedOnLabel}: {generatedOn:dd/MM/yyyy HH:mm}").FontSize(9).FontColor(Colors.Grey.Darken1);
                row.RelativeItem().AlignCenter().Text(labels.PoweredByLabel).FontSize(9).FontColor(Colors.Grey.Darken1);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span(labels.PageLabel + " ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    text.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1);
                    text.Span($" {labels.OfLabel} ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    text.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            }
        });
    }


    private static void AddHeaderCell(TableCellDescriptor header, string text)
    {
        header.Cell()
            .Element(TableHeaderStyle)
            .AlignRight()
            .Text(t =>
            {
                t.Span(text)
                    .Style(HebrewStyle());
            });
    }


    private static IContainer TableHeaderStyle(IContainer container)
    {
        return container
            .Background(Colors.Blue.Lighten4)
            .PaddingVertical(5)
            .PaddingHorizontal(5)
            .BorderBottom(1)
            .BorderColor(Colors.Blue.Lighten1);
    }

    private static IContainer TableCellStyle(IContainer container)
    {
        return container
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten1)
            .PaddingVertical(5)
            .PaddingHorizontal(5);
    }

    private static IContainer AlignForDirection(IContainer container, bool isRtl)
    {
        return isRtl ? container.AlignRight() : container;
    }

    private static void ApplyEmphasis(TextBlockDescriptor descriptor, bool emphasize)
    {
        if (emphasize)
            descriptor.SemiBold().FontSize(12);
    }

    private static decimal RoundMoney(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string FormatDate(DateTime? value)
    {
        return value?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "-";
    }

    private static string NormalizeCurrencyCode(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            return "ILS";

        var normalized = currencyCode.Trim().ToUpperInvariant();
        return normalized switch
        {
            "ILS" or "USD" or "EUR" => normalized,
            _ => "ILS"
        };
    }

    private static string GetCurrencySymbol(string currencyCode)
    {
        return currencyCode switch
        {
            "ILS" => "₪",
            "USD" => "$",
            "EUR" => "€",
            _ => "₪"
        };
    }

    private static string FormatMoney(decimal value, string currencyCode, bool isRtl)
    {
        var symbol = GetCurrencySymbol(currencyCode);
        var formatted = value.ToString("0.00", CultureInfo.InvariantCulture);

        return isRtl ? $"{formatted} {symbol}" : $"{symbol}{formatted}";
    }

    private static InvoicePdfLabels GetLabels(string? language)
    {
        var code = (language ?? "en").Trim().ToLowerInvariant();

        return code switch
        {
            "he" or "he-il" => new InvoicePdfLabels(
                true,
                "חשבונית מס/קבלה",
                "עסק",
                "פרטי לקוח",
                "פרטי חשבונית",
                "שם לקוח",
                "מספר חשבונית",
                "תאריך חשבונית",
                "תאריך יעד",
                "תיאור",
                "כמות",
                "מחיר יחידה",
                "סה\"כ",
                "סכום ביניים",
                "מע\"מ",
                "ניכוי מס במקור",
                "סה\"כ לתשלום",
                "לתשלום בפועל",
                "אופן תשלום",
                "מספר תשלומים",
                "סטטוס",
                "הערות",
                "טלפון",
                "אימייל",
                "הופק בתאריך",
                "עמוד",
                "מתוך",
                "CLIENTA / Powered by CLIENTA",
                "פריט שירות",
                "חתימת העסק"),
            "ar" or "ar-sa" or "ar-eg" => new InvoicePdfLabels(
                true,
                "فاتورة ضريبة/وصل استلام",
                "النشاط التجاري",
                "بيانات العميل",
                "بيانات الفاتورة",
                "اسم العميل",
                "رقم الفاتورة",
                "تاريخ الفاتورة",
                "تاريخ الاستحقاق",
                "الوصف",
                "الكمية",
                "سعر الوحدة",
                "الإجمالي",
                "المجموع الفرعي",
                "ضريبة القيمة المضافة",
                "استقطاع ضريبي",
                "الإجمالي النهائي",
                "المبلغ المستحق",
                "طريقة الدفع",
                "عدد الدفعات",
                "الحالة",
                "ملاحظات",
                "الهاتف",
                "البريد الإلكتروني",
                "تاريخ الإصدار",
                "الصفحة",
                "من",
                "CLIENTA / Powered by CLIENTA",
                "عنصر خدمة",
                "ختم النشاط"),
            _ => new InvoicePdfLabels(
                false,
                "VAT INVOICE",
                "Business",
                "Client Details",
                "Invoice Details",
                "Client Name",
                "Invoice Number",
                "Invoice Date",
                "Due Date",
                "Description",
                "Qty",
                "Unit Price",
                "Total",
                "Subtotal",
                "VAT",
                "Withholding Tax",
                "Total",
                "Final Amount",
                "Payment Method",
                "Installments",
                "Status",
                "Notes",
                "Phone",
                "Email",
                "Generated on",
                "Page",
                "of",
                "CLIENTA / Powered by CLIENTA",
                "Service Item",
                "Business Signature")
        };
    }

    private sealed record InvoicePdfLabels(
        bool IsRtl,
        string InvoiceTitle,
        string BusinessFallback,
        string ClientSectionTitle,
        string InvoiceSectionTitle,
        string ClientNameLabel,
        string InvoiceNumberLabel,
        string InvoiceDateLabel,
        string DueDateLabel,
        string DescriptionLabel,
        string QuantityLabel,
        string PriceLabel,
        string TotalLabel,
        string SubtotalLabel,
        string VatLabel,
        string WithholdingTaxLabel,
        string TotalSectionLabel,
        string FinalAmountLabel,
        string PaymentMethodLabel,
        string InstallmentsLabel,
        string StatusLabel,
        string NotesLabel,
        string PhoneLabel,
        string EmailLabel,
        string GeneratedOnLabel,
        string PageLabel,
        string OfLabel,
        string PoweredByLabel,
        string DefaultLineItem,
        string BusinessSignatureLabel);
}
