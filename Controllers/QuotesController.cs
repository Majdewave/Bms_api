using System.Globalization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/quotes")]
[Authorize(Policy = "manage_quotes")]
public class QuotesController : ControllerBase
{
    private const decimal DefaultVatRate = 18m;
    private const string QuoteNotesSeparator = "\n\n---QUOTE-TERMS---\n\n";

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IFeatureService _featureService;
    private readonly IQuoteConversionService _quoteConversionService;
    private readonly IUserDepartmentFeatureAccessService _userDepartmentFeatureAccessService;
    private readonly IWebHostEnvironment _env;

    public QuotesController(
        AppDbContext db,
        ITenantContext tenantContext,
        IFeatureService featureService,
        IQuoteConversionService quoteConversionService,
        IUserDepartmentFeatureAccessService userDepartmentFeatureAccessService,
        IWebHostEnvironment env)
    {
        _db = db;
        _tenantContext = tenantContext;
        _featureService = featureService;
        _quoteConversionService = quoteConversionService;
        _userDepartmentFeatureAccessService = userDepartmentFeatureAccessService;
        _env = env;
    }

    [HttpGet("departments")]
    public async Task<IActionResult> GetAvailableDepartments()
    {
        if (!await _featureService.IsEnabledAsync("quotes"))
            return Forbid();

        var accessibleDepartmentIds = await GetAccessibleQuoteDepartmentIdsAsync();
        if (accessibleDepartmentIds.Count == 0)
            return Ok(Array.Empty<object>());

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => accessibleDepartmentIds.Contains(d.Id))
            .OrderBy(d => d.DisplayOrder)
            .ThenBy(d => d.Name)
            .Select(d => new { d.Id, d.Name })
            .ToListAsync();

        return Ok(departments);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? dateRange = "last30days",
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? clientId = null,
        [FromQuery] string? sortBy = "newest",
        [FromQuery] string? sortDirection = "desc")
    {
        if (!await _featureService.IsEnabledAsync("quotes"))
            return Forbid();

        var accessibleDepartmentIds = await GetAccessibleQuoteDepartmentIdsAsync();
        if (accessibleDepartmentIds.Count == 0)
            return Ok(new QuoteListResponse(
                Array.Empty<QuoteListItemResponse>(),
                0,
                pageNumber < 1 ? 1 : pageNumber,
                NormalizePageSize(pageSize),
                false));

        var normalizedPageNumber = pageNumber < 1 ? 1 : pageNumber;
        var normalizedPageSize = NormalizePageSize(pageSize);
        var normalizedDateRange = NormalizeDateRange(dateRange);
        var normalizedSearch = search?.Trim();
        var normalizedSortBy = NormalizeSortBy(sortBy);
        var isDescending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        IQueryable<Quote> query = _db.Quotes
            .AsNoTracking()
            .Where(q => q.DepartmentId.HasValue && accessibleDepartmentIds.Contains(q.DepartmentId.Value));

        var dateFrom = ResolveDateFrom(normalizedDateRange);
        if (dateFrom.HasValue)
            query = query.Where(q => q.QuoteDate >= dateFrom.Value);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var searchTerm = normalizedSearch.ToLowerInvariant();
            var parsedSearchStatus = TryParseQuoteStatus(searchTerm);

            query = query.Where(q =>
                q.QuoteNumber.ToLower().Contains(searchTerm) ||
                q.ClientName.ToLower().Contains(searchTerm) ||
                (parsedSearchStatus.HasValue && q.Status == parsedSearchStatus.Value));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsedStatus = TryParseQuoteStatus(status.Trim());
            if (parsedStatus.HasValue)
                query = query.Where(q => q.Status == parsedStatus.Value);
        }

        if (!string.IsNullOrWhiteSpace(clientId) && Guid.TryParse(clientId, out var parsedClientId))
            query = query.Where(q => q.ClientId == parsedClientId);

        query = ApplySorting(query, normalizedSortBy, isDescending);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((normalizedPageNumber - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(q => new QuoteListItemResponse(
                q.Id,
                q.QuoteNumber,
                q.DepartmentId,
                q.ClientId,
                q.ClientName,
                q.QuoteDate,
                q.ValidUntil,
                q.TotalAmount,
                ToApiQuoteStatus(q.Status),
                q.CreatedAt))
            .ToListAsync();

        var hasNextPage = normalizedPageNumber * normalizedPageSize < totalCount;

        return Ok(new QuoteListResponse(
            items,
            totalCount,
            normalizedPageNumber,
            normalizedPageSize,
            hasNextPage));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        if (!await _featureService.IsEnabledAsync("quotes"))
            return Forbid();

        var quote = await _db.Quotes
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (quote == null)
            return NotFound();

        if (!await _userDepartmentFeatureAccessService.IsFeatureEnabledAsync(quote.DepartmentId, "quotesEnabled"))
            return Forbid();

        return Ok(MapQuoteResponse(quote));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateQuoteRequest request)
    {
        if (!await _featureService.IsEnabledAsync("quotes"))
            return Forbid();

        if (!request.DepartmentId.HasValue)
            return BadRequest("Department is required.");

        if (!await _userDepartmentFeatureAccessService.IsFeatureEnabledAsync(request.DepartmentId, "quotesEnabled"))
            return Forbid();

        var customerResult = await ResolveCustomerAsync(request);
        if (!customerResult.Success)
            return BadRequest(customerResult.ErrorMessage);

        var customer = customerResult.Customer!;

        if (!TryBuildLineItems(request.LineItems, out var lineItems, out var lineError))
            return BadRequest(lineError);

        var tenant = await GetTenantForWriteAsync();
        if (tenant == null)
            return NotFound("Tenant not found");

        var quoteNumber = NormalizeQuoteNumber(tenant);
        if (await QuoteNumberExistsAsync(tenant.Id, quoteNumber))
            return Conflict("Quote number already exists.");

        var quote = new Quote
        {
            TenantId = tenant.Id,
            QuoteNumber = quoteNumber,
            DepartmentId = request.DepartmentId,
        };

        ApplyQuoteValues(quote, request, customer, tenant, lineItems, applyTenantSnapshot: true);

        _db.Quotes.Add(quote);
        tenant.NextQuoteNumber = tenant.NextQuoteNumber > 0 ? tenant.NextQuoteNumber + 1 : 2;

        await _db.SaveChangesAsync();

        return Ok(MapQuoteResponse(quote));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateQuoteRequest request)
    {
        if (!await _featureService.IsEnabledAsync("quotes"))
            return Forbid();

        if (!request.DepartmentId.HasValue)
            return BadRequest("Department is required.");

        if (!await _userDepartmentFeatureAccessService.IsFeatureEnabledAsync(request.DepartmentId, "quotesEnabled"))
            return Forbid();

        var quote = await _db.Quotes
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (quote == null)
            return NotFound();

        if (!await _userDepartmentFeatureAccessService.IsFeatureEnabledAsync(quote.DepartmentId, "quotesEnabled"))
            return Forbid();

        var customerResult = await ResolveCustomerAsync(request);
        if (!customerResult.Success)
            return BadRequest(customerResult.ErrorMessage);

        var customer = customerResult.Customer!;

        if (!TryBuildLineItems(request.LineItems, out var lineItems, out var lineError))
            return BadRequest(lineError);

        var tenant = await GetTenantForWriteAsync();
        if (tenant == null)
            return NotFound("Tenant not found");

        _db.QuoteLineItems.RemoveRange(quote.LineItems);
        await _db.SaveChangesAsync();

        quote = await _db.Quotes
            .Include(q => q.LineItems)
            .FirstAsync(q => q.Id == id);

        ApplyQuoteValues(quote, request, customer, tenant, lineItems, applyTenantSnapshot: false);

        foreach (var lineItem in lineItems)
        {
            lineItem.Id = Guid.NewGuid();
            lineItem.QuoteId = quote.Id;
            _db.QuoteLineItems.Add(lineItem);
        }

        await _db.SaveChangesAsync();

        return Ok(MapQuoteResponse(quote));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _featureService.IsEnabledAsync("quotes"))
            return Forbid();

            var quote = await _db.Quotes
                .FirstOrDefaultAsync(q => q.Id == id);
        if (quote == null)
            return NotFound();

            if (!await _userDepartmentFeatureAccessService.IsFeatureEnabledAsync(quote.DepartmentId, "quotesEnabled"))
                return Forbid();

        _db.Quotes.Remove(quote);
        await _db.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("{id}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id)
    {
        if (!await _featureService.IsEnabledAsync("quotes"))
            return Forbid();

        var source = await _db.Quotes
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (source == null)
            return NotFound();

        if (!await _userDepartmentFeatureAccessService.IsFeatureEnabledAsync(source.DepartmentId, "quotesEnabled"))
            return Forbid();

        var tenant = await GetTenantForWriteAsync();
        if (tenant == null)
            return NotFound("Tenant not found");

        var quoteNumber = NormalizeQuoteNumber(tenant);
        if (await QuoteNumberExistsAsync(tenant.Id, quoteNumber))
            return Conflict("Quote number already exists.");

        var duplicate = new Quote
        {
            TenantId = source.TenantId,
            QuoteNumber = quoteNumber,
            DepartmentId = source.DepartmentId,
            ClientId = source.ClientId,
            ClientName = source.ClientName,
            QuoteDate = DateTime.UtcNow,
            ValidUntil = source.ValidUntil,
            Subtotal = source.Subtotal,
            VatRate = source.VatRate,
            VatAmount = source.VatAmount,
            TotalAmount = source.TotalAmount,
            Notes = source.Notes,
            BusinessName = source.BusinessName,
            BusinessAddress = source.BusinessAddress,
            BusinessPhone = source.BusinessPhone,
            BusinessEmail = source.BusinessEmail,
            LogoUrl = source.LogoUrl,
            LogoBase64 = source.LogoBase64,
            Language = source.Language,
            Status = QuoteStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LineItems = source.LineItems.Select(li => new QuoteLineItem
            {
                Description = li.Description,
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
            }).ToList(),
        };

        _db.Quotes.Add(duplicate);
        tenant.NextQuoteNumber = tenant.NextQuoteNumber > 0 ? tenant.NextQuoteNumber + 1 : 2;

        await _db.SaveChangesAsync();

        return Ok(MapQuoteResponse(duplicate));
    }

    [HttpPost("{id}/convert-to-invoice")]
    public async Task<IActionResult> ConvertToInvoice(Guid id)
    {
        if (!await _featureService.IsEnabledAsync("quotes"))
            return Forbid();

        var quote = await _db.Quotes
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (quote == null)
            return NotFound();

        if (!await _userDepartmentFeatureAccessService.IsFeatureEnabledAsync(quote.DepartmentId, "quotesEnabled"))
            return Forbid();

        var tenant = await GetTenantForWriteAsync();
        if (tenant == null)
            return NotFound("Tenant not found");

        var invoiceNumber = NormalizeInvoiceNumber(tenant);
        if (await InvoiceNumberExistsAsync(tenant.Id, invoiceNumber))
            return Conflict("Invoice number already exists.");

        var invoice = _quoteConversionService.BuildInvoiceFromQuote(quote, tenant, invoiceNumber);

        _db.Invoices.Add(invoice);

        quote.Status = QuoteStatus.Converted;
        quote.UpdatedAt = DateTime.UtcNow;

        tenant.NextInvoiceNumber = tenant.NextInvoiceNumber > 0 ? tenant.NextInvoiceNumber + 1 : 2;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            quote = MapQuoteResponse(quote),
            invoiceId = invoice.Id,
            invoiceNumber = invoice.InvoiceNumber,
        });
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id)
    {
        try
        {
            QuestPDF.Drawing.FontManager.RegisterFont(
                System.IO.File.OpenRead(
                    Path.Combine(_env.WebRootPath, "fonts", "NotoSansHebrew-Regular.ttf")));

            if (!await _featureService.IsEnabledAsync("quotes"))
                return Forbid();

            var quote = await _db.Quotes
                .Include(q => q.LineItems)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (quote == null)
                return NotFound();

            if (!await _userDepartmentFeatureAccessService.IsFeatureEnabledAsync(quote.DepartmentId, "quotesEnabled"))
                return Forbid();

            var tenant = await GetTenantAsync();
            var currencyCode = NormalizeCurrencyCode(tenant?.Currency);
            var labels = GetLabels(quote.Language);
            var lineItems = BuildPdfLineItems(quote, labels);
            var businessName = quote.BusinessName ?? tenant?.Name ?? labels.BusinessFallback;
            var businessAddress = quote.BusinessAddress;
            var businessPhone = quote.BusinessPhone ?? tenant?.Phone;
            var businessWhatsApp = tenant?.WhatsApp;
            var (notesText, termsText) = SplitQuoteNotes(quote.Notes);
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var logoBytes = await TryLoadLogoBytesAsync(quote.LogoBase64, quote.LogoUrl ?? tenant?.LogoUrl, baseUrl);
            var generatedOn = DateTime.UtcNow;

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
                            businessAddress,
                            businessPhone,
                            businessWhatsApp));

                    page.Content().PaddingVertical(8).Column(column =>
                    {
                        column.Spacing(10);
                        column.Item().Element(item => ComposeDetailsSection(item, labels, labels.IsRtl, quote, quote.CustomerPhone));
                        column.Item().Element(item => ComposeLineItemsTable(item, labels, labels.IsRtl, lineItems, currencyCode));
                        column.Item().Element(item => ComposeTotalsSection(item, labels, labels.IsRtl, quote, currencyCode));

                        if (!string.IsNullOrWhiteSpace(notesText))
                            column.Item().Element(item => ComposeTextSection(item, labels, labels.IsRtl, labels.NotesLabel, notesText));

                        if (!string.IsNullOrWhiteSpace(termsText))
                            column.Item().Element(item => ComposeTextSection(item, labels, labels.IsRtl, labels.TermsLabel, termsText));
                    });

                    page.Footer().PaddingTop(6).Element(footer =>
                        ComposeFooter(footer, labels, labels.IsRtl, generatedOn));
                });
            }).GeneratePdf();

            return File(pdf, "application/pdf", $"quote-{quote.QuoteNumber}.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.ToString());
        }
    }

    private void ApplyQuoteValues(
        Quote quote,
        CreateQuoteRequest request,
        QuoteCustomerInfo customer,
        Tenant tenant,
        List<QuoteLineItem> lineItems,
        bool applyTenantSnapshot)
    {
        var vatRate = NormalizeVatRate(request.VatRate, tenant);
        var subtotal = RoundMoney(lineItems.Sum(item => item.Quantity * item.UnitPrice));
        var vatAmount = RoundMoney(subtotal * vatRate / 100m);
        var totalAmount = RoundMoney(subtotal + vatAmount);

        quote.ClientId = customer.ClientId;
        quote.DepartmentId = request.DepartmentId;
        quote.ClientName = customer.DisplayName;
        quote.CustomerPhone = customer.Phone;
        quote.CustomerEmail = customer.Email;
        quote.QuoteDate = request.QuoteDate == default ? DateTime.UtcNow : request.QuoteDate;
        quote.ValidUntil = request.ValidUntil;
        quote.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        quote.VatRate = vatRate;
        quote.Subtotal = subtotal;
        quote.VatAmount = vatAmount;
        quote.TotalAmount = totalAmount;
        quote.Status = ParseQuoteStatus(request.Status, quote.Status);
        quote.UpdatedAt = DateTime.UtcNow;

        quote.LineItems.Clear();
        quote.LineItems.AddRange(lineItems);

        if (applyTenantSnapshot)
        {
            quote.BusinessName = tenant.Name;
            quote.BusinessPhone = tenant.Phone;
            quote.BusinessEmail = tenant.OwnerUser?.Email;
            quote.LogoUrl = tenant.LogoUrl;
        }

        quote.Language = ResolveLanguage();
    }

    private static bool TryBuildLineItems(
        List<CreateQuoteLineItemRequest>? requestedLineItems,
        out List<QuoteLineItem> lineItems,
        out string? validationError)
    {
        lineItems = new List<QuoteLineItem>();
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

            if (requestedLineItem.UnitPrice < 0)
            {
                validationError = "Each line item price must be 0 or higher.";
                return false;
            }

            lineItems.Add(new QuoteLineItem
            {
                Description = requestedLineItem.Description.Trim(),
                Quantity = requestedLineItem.Quantity,
                UnitPrice = RoundMoney(requestedLineItem.UnitPrice),
            });
        }

        return true;
    }

    private static QuoteStatus ParseQuoteStatus(string? value, QuoteStatus fallback)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "draft" => QuoteStatus.Draft,
            "sent" => QuoteStatus.Sent,
            "approved" => QuoteStatus.Approved,
            "rejected" => QuoteStatus.Rejected,
            "expired" => QuoteStatus.Expired,
            "converted" => QuoteStatus.Converted,
            _ => fallback,
        };
    }

    private static string ToApiQuoteStatus(QuoteStatus status)
    {
        return status switch
        {
            QuoteStatus.Draft => "draft",
            QuoteStatus.Sent => "sent",
            QuoteStatus.Approved => "approved",
            QuoteStatus.Rejected => "rejected",
            QuoteStatus.Expired => "expired",
            QuoteStatus.Converted => "converted",
            _ => "draft",
        };
    }

    private static QuoteResponse MapQuoteResponse(Quote quote)
    {
        return new QuoteResponse
        {
            Id = quote.Id,
            TenantId = quote.TenantId,
            QuoteNumber = quote.QuoteNumber,
            DepartmentId = quote.DepartmentId,
            ClientId = quote.ClientId,
            ClientName = quote.ClientName,
            QuoteDate = quote.QuoteDate,
            ValidUntil = quote.ValidUntil,
            Subtotal = quote.Subtotal,
            VatRate = quote.VatRate,
            VatAmount = quote.VatAmount,
            TotalAmount = quote.TotalAmount,
            Notes = quote.Notes,
            BusinessName = quote.BusinessName,
            BusinessAddress = quote.BusinessAddress,
            BusinessPhone = quote.BusinessPhone,
            BusinessEmail = quote.BusinessEmail,
            LogoUrl = quote.LogoUrl,
            LogoBase64 = quote.LogoBase64,
            Language = quote.Language,
            Status = ToApiQuoteStatus(quote.Status),
            CreatedAt = quote.CreatedAt,
            UpdatedAt = quote.UpdatedAt,
            LineItems = quote.LineItems
                .Select(li => new QuoteLineItemResponse
                {
                    Id = li.Id,
                    QuoteId = li.QuoteId,
                    Description = li.Description,
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice,
                    Total = RoundMoney(li.Total),
                })
                .ToList(),
        };
    }

    private string ResolveLanguage()
    {
        var requestedLanguage = Request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(requestedLanguage))
            return "en";

        var firstLanguage = requestedLanguage
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstLanguage) ? "en" : firstLanguage.ToLowerInvariant();
    }

    private List<QuoteLineItem> BuildPdfLineItems(Quote quote, QuotePdfLabels labels)
    {
        if (quote.LineItems.Count > 0)
            return quote.LineItems;

        return new List<QuoteLineItem>
        {
            new()
            {
                Description = labels.DefaultLineItem,
                Quantity = 1,
                UnitPrice = quote.Subtotal > 0 ? quote.Subtotal : quote.TotalAmount,
            }
        };
    }

    private static TextStyle HebrewStyle()
    {
        return TextStyle.Default.FontFamily("Noto Sans Hebrew").DirectionFromRightToLeft();
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
        QuotePdfLabels labels,
        bool isRtl,
        byte[]? logoBytes,
        string businessName,
        string? businessAddress,
        string? businessPhone,
        string? businessWhatsApp)
    {
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

                            if (!string.IsNullOrWhiteSpace(businessAddress))
                            {
                                var addressText = rightColumn.Item()
                                    .AlignRight()
                                    .Text(businessAddress.Trim())
                                    .FontSize(11)
                                    .FontColor(Colors.Grey.Darken3);

                                if (isRtl)
                                    addressText.Style(HebrewStyle());
                            }

                        });
                });

                var title = column.Item()
                    .PaddingTop(2)
                    .AlignCenter()
                    .Text(labels.DocumentTitle)
                    .FontSize(20)
                    .Bold()
                    .FontColor(Colors.Blue.Darken2);

                if (isRtl)
                    title.Style(HebrewStyle());
            });
    }

    private static void ComposeDetailsSection(IContainer container, QuotePdfLabels labels, bool isRtl, Quote quote, string? clientPhone)
    {
        container.Row(row =>
        {
            row.Spacing(14);

            if (isRtl)
            {
                row.RelativeItem().Element(item => ComposeMetaCard(item, content => ComposeQuoteMeta(content, labels, true, quote)));
                row.RelativeItem().Element(item => ComposeMetaCard(item, content => ComposeClientMeta(content, labels, true, quote, clientPhone)));
            }
            else
            {
                row.RelativeItem().Element(item => ComposeMetaCard(item, content => ComposeClientMeta(content, labels, false, quote, clientPhone)));
                row.RelativeItem().Element(item => ComposeMetaCard(item, content => ComposeQuoteMeta(content, labels, false, quote)));
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
        QuotePdfLabels labels,
        bool isRtl,
        Quote quote,
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

                column.Item().AlignRight().Text(label).Bold().FontFamily("Noto Sans Hebrew");

                var valueText = column.Item().AlignRight().Text(value).FontFamily("Noto Sans Hebrew");

                if (isRtl)
                    valueText.Style(HebrewStyle());

                column.Item().PaddingBottom(2);
            }

            AddField(labels.ClientNameLabel, quote.ClientName);
            AddField(labels.PhoneLabel, clientPhone);
            AddField(labels.EmailLabel, quote.CustomerEmail);
        });
    }

    private static void ComposeQuoteMeta(
        IContainer container,
        QuotePdfLabels labels,
        bool isRtl,
        Quote quote)
    {
        AlignForDirection(container, isRtl).Column(column =>
        {
            column.Spacing(4);

            var title = column.Item()
                .Text(labels.DocumentSectionTitle)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2)
                .FontSize(12);

            if (isRtl)
                title.Style(HebrewStyle());

            void AddField(string label, string value)
            {
                column.Item().AlignRight().Text(label).Bold().FontFamily("Noto Sans Hebrew");

                var valueText = column.Item().AlignRight().Text(value).FontFamily("Noto Sans Hebrew");

                if (isRtl)
                    valueText.Style(HebrewStyle());

                column.Item().PaddingBottom(2);
            }

            AddField(labels.DocumentNumberLabel, quote.QuoteNumber);
            AddField(labels.DocumentDateLabel, FormatDate(quote.QuoteDate));
            AddField(labels.ValidUntilLabel, FormatDate(quote.ValidUntil));
        });
    }

    private static void ComposeLineItemsTable(IContainer container, QuotePdfLabels labels, bool isRtl, List<QuoteLineItem> lineItems, string currencyCode)
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
                    table.Cell().Element(TableCellStyle).AlignRight().Text(FormatMoney(lineItem.UnitPrice, currencyCode, true));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(lineItem.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(text =>
                    {
                        text.Span(lineItem.Description).Style(HebrewStyle());
                    });
                }
                else
                {
                    table.Cell().Element(TableCellStyle).Text(lineItem.Description);
                    table.Cell().Element(TableCellStyle).AlignRight().Text(lineItem.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(FormatMoney(lineItem.UnitPrice, currencyCode, false));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(FormatMoney(lineItem.Total, currencyCode, false));
                }
            }
        });
    }

    private static void ComposeTotalsSection(IContainer container, QuotePdfLabels labels, bool isRtl, Quote quote, string currencyCode)
    {
        container.AlignRight().Width(260).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(column =>
        {
            column.Spacing(6);
            column.Item().Element(item => ComposeTotalRow(item, labels.SubtotalLabel, FormatMoney(quote.Subtotal, currencyCode, isRtl), isRtl, false));
            column.Item().Element(item => ComposeTotalRow(item, $"{labels.VatLabel} ({quote.VatRate:0.##}%)", FormatMoney(quote.VatAmount, currencyCode, isRtl), isRtl, false));
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            column.Item().Element(item => ComposeTotalRow(item, labels.TotalLabel, FormatMoney(quote.TotalAmount, currencyCode, isRtl), isRtl, true));
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

    private static void ComposeTextSection(
        IContainer container,
        QuotePdfLabels labels,
        bool isRtl,
        string sectionTitle,
        string sectionText)
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
                    .Text(sectionTitle)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken2);

                if (isRtl)
                    title.Style(HebrewStyle());

                var body = column.Item()
                    .AlignRight()
                    .Text(sectionText);

                if (isRtl)
                    body.Style(HebrewStyle());
            });
    }

    private static void ComposeFooter(IContainer container, QuotePdfLabels labels, bool isRtl, DateTime generatedOn)
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

    private static int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
            return 20;

        return Math.Min(pageSize, 100);
    }

    private static string NormalizeDateRange(string? dateRange)
    {
        var normalized = dateRange?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "last3months" => "last3months",
            "lastyear" => "lastyear",
            "all" => "all",
            _ => "last30days",
        };
    }

    private static DateTime? ResolveDateFrom(string dateRange)
    {
        var now = DateTime.UtcNow;

        return dateRange switch
        {
            "last3months" => now.AddMonths(-3),
            "lastyear" => now.AddYears(-1),
            "all" => null,
            _ => now.AddDays(-30),
        };
    }

    private static string NormalizeSortBy(string? sortBy)
    {
        var normalized = sortBy?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "date" => "date",
            "quotenumber" => "quotenumber",
            "client" => "client",
            "status" => "status",
            "amount" => "amount",
            _ => "newest",
        };
    }

    private static IQueryable<Quote> ApplySorting(IQueryable<Quote> query, string sortBy, bool descending)
    {
        return sortBy switch
        {
            "date" => descending ? query.OrderByDescending(q => q.QuoteDate) : query.OrderBy(q => q.QuoteDate),
            "quotenumber" => descending ? query.OrderByDescending(q => q.QuoteNumber) : query.OrderBy(q => q.QuoteNumber),
            "client" => descending ? query.OrderByDescending(q => q.ClientName) : query.OrderBy(q => q.ClientName),
            "status" => descending ? query.OrderByDescending(q => q.Status) : query.OrderBy(q => q.Status),
            "amount" => descending ? query.OrderByDescending(q => q.TotalAmount) : query.OrderBy(q => q.TotalAmount),
            _ => descending ? query.OrderByDescending(q => q.CreatedAt) : query.OrderBy(q => q.CreatedAt),
        };
    }

    private static QuoteStatus? TryParseQuoteStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "draft" => QuoteStatus.Draft,
            "sent" => QuoteStatus.Sent,
            "approved" => QuoteStatus.Approved,
            "rejected" => QuoteStatus.Rejected,
            "expired" => QuoteStatus.Expired,
            "converted" => QuoteStatus.Converted,
            _ => null,
        };
    }

    private static (string? NotesText, string? TermsText) SplitQuoteNotes(string? rawNotes)
    {
        if (string.IsNullOrWhiteSpace(rawNotes))
            return (null, null);

        var normalized = rawNotes.Trim();
        var separatorIndex = normalized.IndexOf(QuoteNotesSeparator, StringComparison.Ordinal);

        if (separatorIndex < 0)
            return (normalized, null);

        var notesText = normalized[..separatorIndex].Trim();
        var termsText = normalized[(separatorIndex + QuoteNotesSeparator.Length)..].Trim();

        return (
            string.IsNullOrWhiteSpace(notesText) ? null : notesText,
            string.IsNullOrWhiteSpace(termsText) ? null : termsText
        );
    }

    private static QuotePdfLabels GetLabels(string? language)
    {
        var code = (language ?? "en").Trim().ToLowerInvariant();

        return code switch
        {
            "he" or "he-il" => new QuotePdfLabels(
                true,
                "הצעת מחיר",
                "עסק",
                "פרטי לקוח",
                "פרטי הצעת מחיר",
                "שם לקוח",
                "מספר הצעה",
                "תאריך הצעה",
                "בתוקף עד",
                "תיאור",
                "כמות",
                "מחיר יחידה",
                "סה\"כ",
                "סכום ביניים",
                "מע\"מ",
                "הערות",
                "תנאי הצעת מחיר",
                "טלפון",
                "אימייל",
                "הופק בתאריך",
                "עמוד",
                "מתוך",
                "CLIENTA / Powered by CLIENTA",
                "פריט שירות"),
            "ar" or "ar-sa" or "ar-eg" => new QuotePdfLabels(
                true,
                "عرض سعر",
                "النشاط التجاري",
                "بيانات العميل",
                "بيانات عرض السعر",
                "اسم العميل",
                "رقم العرض",
                "تاريخ العرض",
                "صالح حتى",
                "الوصف",
                "الكمية",
                "سعر الوحدة",
                "الإجمالي",
                "المجموع الفرعي",
                "ضريبة القيمة المضافة",
                "ملاحظات",
                "شروط عرض السعر",
                "الهاتف",
                "البريد الإلكتروني",
                "تاريخ الإصدار",
                "الصفحة",
                "من",
                "CLIENTA / Powered by CLIENTA",
                "عنصر خدمة"),
            _ => new QuotePdfLabels(
                false,
                "QUOTE",
                "Business",
                "Client Details",
                "Quote Details",
                "Client Name",
                "Quote Number",
                "Quote Date",
                "Valid Until",
                "Description",
                "Qty",
                "Unit Price",
                "Total",
                "Subtotal",
                "VAT",
                "Notes",
                "Quote Terms",
                "Phone",
                "Email",
                "Generated on",
                "Page",
                "of",
                "CLIENTA / Powered by CLIENTA",
                "Service Item")
        };
    }

    private sealed record QuotePdfLabels(
        bool IsRtl,
        string DocumentTitle,
        string BusinessFallback,
        string ClientSectionTitle,
        string DocumentSectionTitle,
        string ClientNameLabel,
        string DocumentNumberLabel,
        string DocumentDateLabel,
        string ValidUntilLabel,
        string DescriptionLabel,
        string QuantityLabel,
        string PriceLabel,
        string TotalLabel,
        string SubtotalLabel,
        string VatLabel,
        string NotesLabel,
        string TermsLabel,
        string PhoneLabel,
        string EmailLabel,
        string GeneratedOnLabel,
        string PageLabel,
        string OfLabel,
        string PoweredByLabel,
        string DefaultLineItem);

    private sealed record QuoteListItemResponse(
        Guid Id,
        string QuoteNumber,
        Guid? DepartmentId,
        Guid? ClientId,
        string ClientName,
        DateTime QuoteDate,
        DateTime? ValidUntil,
        decimal TotalAmount,
        string Status,
        DateTime CreatedAt);

    private sealed record QuoteListResponse(
        IReadOnlyList<QuoteListItemResponse> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        bool HasNextPage);

    private async Task<Tenant?> GetTenantAsync()
    {
        if (_tenantContext.TenantId != Guid.Empty)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Include(t => t.OwnerUser)
                .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

            if (tenant != null)
                return tenant;
        }

        return await _db.Tenants
            .AsNoTracking()
            .Include(t => t.OwnerUser)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private async Task<Tenant?> GetTenantForWriteAsync()
    {
        if (_tenantContext.TenantId != Guid.Empty)
        {
            var tenant = await _db.Tenants
                .Include(t => t.OwnerUser)
                .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

            if (tenant != null)
                return tenant;
        }

        return await _db.Tenants
            .Include(t => t.OwnerUser)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private async Task<bool> QuoteNumberExistsAsync(Guid tenantId, string quoteNumber)
    {
        return await _db.Quotes
            .IgnoreQueryFilters()
            .AnyAsync(quote => quote.TenantId == tenantId && quote.QuoteNumber == quoteNumber);
    }

    private async Task<bool> InvoiceNumberExistsAsync(Guid tenantId, string invoiceNumber)
    {
        return await _db.Invoices
            .IgnoreQueryFilters()
            .AnyAsync(invoice => invoice.TenantId == tenantId && invoice.InvoiceNumber == invoiceNumber);
    }

    private async Task<HashSet<Guid>> GetAccessibleQuoteDepartmentIdsAsync()
    {
        var tenantId = _tenantContext.TenantId;
        var userId = _tenantContext.UserId;

        if (tenantId == Guid.Empty || userId is null || userId.Value == Guid.Empty)
            return new HashSet<Guid>();

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId.Value && u.TenantId == tenantId);

        var isAdmin = string.Equals(user?.Role, "Admin", StringComparison.OrdinalIgnoreCase);

        IQueryable<Guid> baseDepartmentIdsQuery = _db.Departments
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.IsActive)
            .Select(d => d.Id);

        if (!isAdmin)
        {
            var assignedDepartmentIds = _db.StaffDepartments
                .AsNoTracking()
                .Where(sd => sd.TenantId == tenantId && sd.Staff.UserId == userId.Value)
                .Select(sd => sd.DepartmentId)
                .Distinct();

            baseDepartmentIdsQuery = baseDepartmentIdsQuery.Where(id => assignedDepartmentIds.Contains(id));
        }

        var departmentIds = await baseDepartmentIdsQuery.Distinct().ToListAsync();
        if (departmentIds.Count == 0)
            return new HashSet<Guid>();

        var departmentFeatureRows = await _db.DepartmentFeatures
            .AsNoTracking()
            .Where(df => departmentIds.Contains(df.DepartmentId))
            .Select(df => new { df.DepartmentId, df.FeatureKey, df.IsEnabled })
            .ToListAsync();

        var byDepartment = departmentFeatureRows
            .GroupBy(row => row.DepartmentId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var allowed = new HashSet<Guid>();

        foreach (var departmentId in departmentIds)
        {
            if (!byDepartment.TryGetValue(departmentId, out var rows) || rows.Count == 0)
            {
                allowed.Add(departmentId);
                continue;
            }

            var quotesFlag = rows
                .FirstOrDefault(row => string.Equals(row.FeatureKey, "quotesEnabled", StringComparison.OrdinalIgnoreCase));

            if (quotesFlag == null || quotesFlag.IsEnabled)
            {
                allowed.Add(departmentId);
            }
        }

        return allowed;
    }

    private static string NormalizeQuoteNumber(Tenant tenant)
    {
        var prefix = string.IsNullOrWhiteSpace(tenant.QuotePrefix) ? "QT-" : tenant.QuotePrefix.Trim();
        var next = tenant.NextQuoteNumber > 0 ? tenant.NextQuoteNumber : 1;
        return $"{prefix}{next}";
    }

    private static string NormalizeInvoiceNumber(Tenant tenant)
    {
        var prefix = string.IsNullOrWhiteSpace(tenant.InvoicePrefix) ? "INV-" : tenant.InvoicePrefix.Trim();
        var next = tenant.NextInvoiceNumber > 0 ? tenant.NextInvoiceNumber : 1;
        return $"{prefix}{next}";
    }

    private static decimal NormalizeVatRate(decimal? requestedVatRate, Tenant? tenant)
    {
        if (requestedVatRate.HasValue)
        {
            var requested = requestedVatRate.Value;
            if (requested >= 0 && requested <= 100)
                return RoundMoney(requested);

            return DefaultVatRate;
        }

        if (tenant is not null && tenant.DefaultVatRate >= 0 && tenant.DefaultVatRate <= 100)
            return RoundMoney(tenant.DefaultVatRate);

        return DefaultVatRate;
    }

    private static decimal RoundMoney(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private async Task<QuoteCustomerInfoResult> ResolveCustomerAsync(CreateQuoteRequest request)
    {
        var treatAsExistingClient = request.IsExistingClient || request.ClientId.HasValue;

        if (treatAsExistingClient)
        {
            if (!request.ClientId.HasValue || request.ClientId.Value == Guid.Empty)
            {
                return QuoteCustomerInfoResult.Fail("Client is required for an existing customer quote.");
            }

            var client = await _db.Clients.FindAsync(request.ClientId.Value);
            if (client == null)
            {
                return QuoteCustomerInfoResult.Fail("Client not found");
            }

            return QuoteCustomerInfoResult.Ok(new QuoteCustomerInfo(
                client.Id,
                client.FullName,
                client.Phone,
                client.Email));
        }

        var customerName = request.CustomerName?.Trim();
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return QuoteCustomerInfoResult.Fail("Customer full name is required.");
        }

        return QuoteCustomerInfoResult.Ok(new QuoteCustomerInfo(
            null,
            customerName,
            request.CustomerPhone?.Trim(),
            request.CustomerEmail?.Trim()));
    }

    private sealed record QuoteCustomerInfo(Guid? ClientId, string DisplayName, string? Phone, string? Email);

    private sealed record QuoteCustomerInfoResult(bool Success, string? ErrorMessage, QuoteCustomerInfo? Customer)
    {
        public static QuoteCustomerInfoResult Ok(QuoteCustomerInfo customer) => new(true, null, customer);
        public static QuoteCustomerInfoResult Fail(string errorMessage) => new(false, errorMessage, null);
    }

}
