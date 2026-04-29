using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFeatureService _featureService;
    private readonly ITenantContext _tenantContext;

    public InvoicesController(AppDbContext db, ITenantContext tenantContext, IFeatureService featureService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _featureService = featureService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!await _featureService.IsEnabledAsync("invoices"))
            return Forbid();

        var invoices = await _db.Invoices
            .Include(i => i.LineItems)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(invoices);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Invoice request)
    {
        if (!await _featureService.IsEnabledAsync("invoices"))
            return Forbid();

        var client = await _db.Clients.FindAsync(request.ClientId);

        if (client == null)
            return BadRequest("Client not found");

        var invoice = new Invoice
        {
            InvoiceNumber = request.InvoiceNumber,
            ClientId = client.Id,
            ClientName = client.FullName,
            Amount = request.Amount,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            Notes = request.Notes,
            BusinessName = request.BusinessName,
            BusinessAddress = request.BusinessAddress,
            BusinessPhone = request.BusinessPhone,
            BusinessEmail = request.BusinessEmail,
            LogoUrl = request.LogoUrl,
            LogoBase64 = request.LogoBase64,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language,
            LineItems = BuildRequestedLineItems(request)
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        return Ok(invoice);
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id)
    {
        if (!await _featureService.IsEnabledAsync("invoices"))
            return Forbid();

        var invoice = await _db.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null)
            return NotFound();

        var tenant = await GetTenantAsync();
        var lang = string.IsNullOrWhiteSpace(invoice.Language) ? "he" : invoice.Language.ToLowerInvariant();
        var isRtl = lang == "he" || lang == "ar";
        var title = lang switch
        {
            "he" => "חשבונית",
            "ar" => "فاتورة",
            _ => "INVOICE"
        };
        var client = lang switch
        {
            "he" => "לקוח",
            "ar" => "العميل",
            _ => "Client"
        };
        var dateLabel = lang switch
        {
            "he" => "תאריך",
            "ar" => "التاريخ",
            _ => "Date"
        };
        var dueLabel = lang switch
        {
            "he" => "תאריך יעד",
            "ar" => "تاريخ الاستحقاق",
            _ => "Due Date"
        };
        var totalLabel = lang switch
        {
            "he" => "סה\"כ",
            "ar" => "الإجمالي",
            _ => "Total"
        };
        var notesLabel = lang switch
        {
            "he" => "הערות",
            "ar" => "ملاحظات",
            _ => "Notes"
        };
        var descriptionLabel = lang switch
        {
            "he" => "תיאור",
            "ar" => "الوصف",
            _ => "Description"
        };
        var qtyLabel = lang switch
        {
            "he" => "כמות",
            "ar" => "الكمية",
            _ => "Qty"
        };
        var priceLabel = lang switch
        {
            "he" => "מחיר",
            "ar" => "السعر",
            _ => "Price"
        };
        var businessName = invoice.BusinessName ?? tenant?.Name ?? "DigitalPenPro";
        var businessAddress = invoice.BusinessAddress;
        var businessPhone = invoice.BusinessPhone ?? "+971 50 123 4567";
        var businessEmail = invoice.BusinessEmail ?? "info@digitalpenpro.com";
        var logoBytes = await TryLoadLogoBytesAsync(invoice.LogoBase64, invoice.LogoUrl ?? tenant?.LogoUrl);
        var lineItems = invoice.LineItems.Any()
            ? invoice.LineItems.Select(item => new
            {
                Description = string.IsNullOrWhiteSpace(item.Description) ? "Service" : item.Description,
                Quantity = item.Quantity <= 0 ? 1 : item.Quantity,
                Price = item.Price,
                Total = item.Total <= 0 ? item.Price * (item.Quantity <= 0 ? 1 : item.Quantity) : item.Total,
            }).ToList()
            : new[]
            {
                new
                {
                    Description = "Service",
                    Quantity = 1m,
                    Price = invoice.Amount,
                    Total = invoice.Amount,
                }
            }.ToList();
        var total = lineItems.Sum(x => x.Total);

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);

                page.Content().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        if (!isRtl)
                        {
                            if (logoBytes is { Length: > 0 })
                            {
                                row.ConstantItem(70)
                                    .Height(50)
                                    .AlignMiddle()
                                    .Image(logoBytes);

                                row.ConstantItem(15);
                            }

                            row.RelativeItem().Column(c =>
                            {
                                c.Item().AlignLeft().Text(businessName).Bold().FontSize(18);

                                if (!string.IsNullOrWhiteSpace(businessAddress))
                                    c.Item().AlignLeft().Text(businessAddress);

                                c.Item().AlignLeft().Text(businessEmail);
                                c.Item().AlignLeft().Text(businessPhone);
                            });

                            row.ConstantItem(200).Column(c =>
                            {
                                c.Item().AlignRight().Text(title).FontSize(24).Bold();
                                c.Item().AlignRight().Text($"# {invoice.InvoiceNumber}");
                                c.Item().AlignRight().Text($"{dateLabel}: {invoice.InvoiceDate:yyyy-MM-dd}");
                                c.Item().AlignRight().Text($"{dueLabel}: {invoice.DueDate?.ToString("yyyy-MM-dd") ?? "-"}");
                            });
                        }
                        else
                        {
                            row.ConstantItem(200).Column(c =>
                            {
                                c.Item().AlignRight().Text(title).FontSize(24).Bold();
                                c.Item().AlignRight().Text($"# {invoice.InvoiceNumber}");
                                c.Item().AlignRight().Text($"{dateLabel}: {invoice.InvoiceDate:yyyy-MM-dd}");
                                c.Item().AlignRight().Text($"{dueLabel}: {invoice.DueDate?.ToString("yyyy-MM-dd") ?? "-"}");
                            });

                            row.RelativeItem().Column(c =>
                            {
                                c.Item().AlignRight().Text(businessName).Bold().FontSize(18);

                                if (!string.IsNullOrWhiteSpace(businessAddress))
                                    c.Item().AlignRight().Text(businessAddress);

                                c.Item().AlignRight().Text(businessEmail);
                                c.Item().AlignRight().Text(businessPhone);
                            });

                            if (logoBytes is { Length: > 0 })
                            {
                                row.ConstantItem(15);
                                row.ConstantItem(70)
                                    .Height(50)
                                    .AlignMiddle()
                                    .Image(logoBytes);
                            }
                        }
                    });

                    col.Item().PaddingVertical(10).LineHorizontal(1);

                    if (isRtl)
                    {
                        col.Item().AlignRight().Text($"{client}: {invoice.ClientName}").Bold();
                        col.Item().AlignRight().Text($"{dateLabel}: {invoice.InvoiceDate:yyyy-MM-dd}");
                        col.Item().AlignRight().Text($"{dueLabel}: {invoice.DueDate?.ToString("yyyy-MM-dd") ?? "-"}");
                    }
                    else
                    {
                        col.Item().AlignLeft().Text($"{client}: {invoice.ClientName}").Bold();
                        col.Item().AlignLeft().Text($"{dateLabel}: {invoice.InvoiceDate:yyyy-MM-dd}");
                        col.Item().AlignLeft().Text($"{dueLabel}: {invoice.DueDate?.ToString("yyyy-MM-dd") ?? "-"}");
                    }

                    col.Item().PaddingVertical(10);

                    col.Item().Table(table =>
                    {
                        if (isRtl)
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(4);
                            });
                        }
                        else
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });
                        }

                        table.Header(header =>
                        {
                            if (isRtl)
                            {
                                header.Cell().PaddingBottom(5).AlignRight().Text(totalLabel).Bold();
                                header.Cell().PaddingBottom(5).AlignRight().Text(priceLabel).Bold();
                                header.Cell().PaddingBottom(5).AlignRight().Text(qtyLabel).Bold();
                                header.Cell().PaddingBottom(5).AlignRight().Text(descriptionLabel).Bold();
                            }
                            else
                            {
                                header.Cell().PaddingBottom(5).Text(descriptionLabel).Bold();
                                header.Cell().PaddingBottom(5).AlignRight().Text(qtyLabel).Bold();
                                header.Cell().PaddingBottom(5).AlignRight().Text(priceLabel).Bold();
                                header.Cell().PaddingBottom(5).AlignRight().Text(totalLabel).Bold();
                            }
                        });

                        foreach (var item in lineItems)
                        {
                            if (isRtl)
                            {
                                table.Cell().PaddingVertical(4).AlignRight().Text($"{item.Total:0.00}");
                                table.Cell().PaddingVertical(4).AlignRight().Text($"{item.Price:0.00}");
                                table.Cell().PaddingVertical(4).AlignRight().Text($"{item.Quantity:0.##}");
                                table.Cell().PaddingVertical(4).AlignRight().Text(item.Description);
                            }
                            else
                            {
                                table.Cell().PaddingVertical(4).Text(item.Description);
                                table.Cell().PaddingVertical(4).AlignRight().Text($"{item.Quantity:0.##}");
                                table.Cell().PaddingVertical(4).AlignRight().Text($"{item.Price:0.00}");
                                table.Cell().PaddingVertical(4).AlignRight().Text($"{item.Total:0.00}");
                            }
                        }
                    });

                    col.Item().PaddingVertical(10).LineHorizontal(1);

                    if (isRtl)
                    {
                        col.Item().AlignRight().Text($"{totalLabel}: {total:0.00}")
                            .FontSize(16)
                            .Bold();
                    }
                    else
                    {
                        col.Item().AlignLeft().Text($"{totalLabel}: {total:0.00}")
                            .FontSize(16)
                            .Bold();
                    }

                    if (!string.IsNullOrEmpty(invoice.Notes))
                    {
                        if (isRtl)
                        {
                            col.Item().PaddingTop(10).AlignRight().Text(notesLabel).Bold();
                            col.Item().AlignRight().Text(invoice.Notes);
                        }
                        else
                        {
                            col.Item().PaddingTop(10).AlignLeft().Text(notesLabel).Bold();
                            col.Item().AlignLeft().Text(invoice.Notes);
                        }
                    }
                });
            });
        }).GeneratePdf();

        return File(pdf, "application/pdf", $"invoice-{invoice.Id}.pdf");
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Invoice request)
    {
        if (!await _featureService.IsEnabledAsync("invoices"))
            return Forbid();

        var invoice = await _db.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null)
            return NotFound();

        var client = await _db.Clients.FindAsync(request.ClientId);

        if (client == null)
            return BadRequest("Client not found");

        invoice.InvoiceNumber = request.InvoiceNumber;
        invoice.ClientId = client.Id;
        invoice.ClientName = client.FullName;
        invoice.Amount = request.Amount;
        invoice.InvoiceDate = request.InvoiceDate;
        invoice.DueDate = request.DueDate;
        invoice.Notes = request.Notes;
        invoice.BusinessName = request.BusinessName;
        invoice.BusinessAddress = request.BusinessAddress;
        invoice.BusinessPhone = request.BusinessPhone;
        invoice.BusinessEmail = request.BusinessEmail;
        invoice.LogoUrl = request.LogoUrl;
        invoice.LogoBase64 = request.LogoBase64;
        invoice.Language = string.IsNullOrWhiteSpace(request.Language) ? invoice.Language : request.Language;

        invoice.LineItems.Clear();

        foreach (var lineItem in BuildRequestedLineItems(request))
        {
            invoice.LineItems.Add(lineItem);
        }

        await _db.SaveChangesAsync();

        return Ok(invoice);
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

    private List<InvoiceLineItem> BuildRequestedLineItems(Invoice request)
    {
        if (request.LineItems == null || request.LineItems.Count == 0)
            return new List<InvoiceLineItem>();

        return request.LineItems
            .Where(x => !string.IsNullOrWhiteSpace(x.Description))
            .Select(x => new InvoiceLineItem
            {
                Description = x.Description,
                Quantity = x.Quantity <= 0 ? 1 : x.Quantity,
                Price = x.Price
            })
            .ToList();
    }

    private List<InvoiceLineItem> BuildPdfLineItems(Invoice invoice, InvoicePdfLabels labels)
    {
        if (invoice.LineItems != null && invoice.LineItems.Count > 0)
            return invoice.LineItems;

        return new List<InvoiceLineItem>
        {
            new InvoiceLineItem
            {
                Description = labels.DefaultLineItem,
                Quantity = 1,
                Price = invoice.Amount
            }
        };
    }

    private async Task<Tenant?> GetTenantAsync()
    {
        if (_tenantContext.TenantId != Guid.Empty)
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);
            if (tenant != null)
                return tenant;
        }

        return await _db.Tenants.AsNoTracking().OrderBy(t => t.CreatedAt).FirstOrDefaultAsync();
    }

    private static async Task<byte[]?> TryLoadLogoBytesAsync(string? logoBase64, string? logoUrl)
    {
        var base64Bytes = TryDecodeBase64(logoBase64);
        if (base64Bytes != null)
            return base64Bytes;

        if (string.IsNullOrWhiteSpace(logoUrl) || !Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
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
        string? businessAddress,
        string? businessPhone,
        string? businessEmail)
    {
        container.Row(row =>
        {
            if (isRtl)
            {
                row.RelativeItem().Element(x => ComposeBusinessBlock(x, labels, true, businessName, businessAddress, businessPhone, businessEmail));

                if (logoBytes != null)
                    row.ConstantItem(90).Height(70).AlignLeft().Image(logoBytes).FitArea();
            }
            else
            {
                if (logoBytes != null)
                    row.ConstantItem(90).Height(70).Image(logoBytes).FitArea();

                row.RelativeItem().Element(x => ComposeBusinessBlock(x, labels, false, businessName, businessAddress, businessPhone, businessEmail));
            }
        });
    }

    private static void ComposeBusinessBlock(
        IContainer container,
        InvoicePdfLabels labels,
        bool isRtl,
        string businessName,
        string? businessAddress,
        string? businessPhone,
        string? businessEmail)
    {
        AlignForDirection(container, isRtl).Column(col =>
        {
            col.Spacing(3);
            col.Item().Text(labels.InvoiceTitle).FontSize(28).Bold().FontColor(Colors.Blue.Darken2);
            col.Item().Text(businessName).FontSize(14).SemiBold();

            if (!string.IsNullOrWhiteSpace(businessAddress))
                col.Item().Text(businessAddress);

            if (!string.IsNullOrWhiteSpace(businessPhone))
                col.Item().Text($"{labels.PhoneLabel}: {businessPhone}");

            if (!string.IsNullOrWhiteSpace(businessEmail))
                col.Item().Text($"{labels.EmailLabel}: {businessEmail}");
        });
    }

    private static void ComposeDetailsSection(IContainer container, InvoicePdfLabels labels, bool isRtl, Invoice invoice)
    {
        container.Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Background(Colors.Grey.Lighten5)
            .Padding(14)
            .Row(row =>
            {
                if (isRtl)
                {
                    row.RelativeItem().Element(x => ComposeInvoiceMeta(x, labels, true, invoice));
                    row.RelativeItem().Element(x => ComposeClientMeta(x, labels, true, invoice));
                }
                else
                {
                    row.RelativeItem().Element(x => ComposeClientMeta(x, labels, false, invoice));
                    row.RelativeItem().Element(x => ComposeInvoiceMeta(x, labels, false, invoice));
                }
            });
    }

    private static void ComposeClientMeta(IContainer container, InvoicePdfLabels labels, bool isRtl, Invoice invoice)
    {
        AlignForDirection(container, isRtl).Column(col =>
        {
            col.Spacing(4);
            col.Item().Text(labels.ClientSectionTitle).SemiBold().FontColor(Colors.Blue.Darken2);
            col.Item().Text($"{labels.ClientNameLabel}: {invoice.ClientName}");
        });
    }

    private static void ComposeInvoiceMeta(IContainer container, InvoicePdfLabels labels, bool isRtl, Invoice invoice)
    {
        AlignForDirection(container, isRtl).Column(col =>
        {
            col.Spacing(4);
            col.Item().Text(labels.InvoiceSectionTitle).SemiBold().FontColor(Colors.Blue.Darken2);
            col.Item().Text($"{labels.InvoiceNumberLabel}: {invoice.InvoiceNumber}");
            col.Item().Text($"{labels.InvoiceDateLabel}: {FormatDate(invoice.InvoiceDate)}");
            col.Item().Text($"{labels.DueDateLabel}: {FormatDate(invoice.DueDate)}");
        });
    }

    private static void ComposeLineItemsTable(IContainer container, InvoicePdfLabels labels, bool isRtl, List<InvoiceLineItem> lineItems)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(70);
                columns.ConstantColumn(80);
                columns.ConstantColumn(90);
            });

            table.Header(header =>
            {
                if (isRtl)
                {
                    header.Cell().Element(TableHeaderStyle).AlignRight().Text(labels.TotalLabel);
                    header.Cell().Element(TableHeaderStyle).AlignRight().Text(labels.PriceLabel);
                    header.Cell().Element(TableHeaderStyle).AlignRight().Text(labels.QuantityLabel);
                    header.Cell().Element(TableHeaderStyle).AlignRight().Text(labels.DescriptionLabel);
                }
                else
                {
                    header.Cell().Element(TableHeaderStyle).Text(labels.DescriptionLabel);
                    header.Cell().Element(TableHeaderStyle).Text(labels.QuantityLabel);
                    header.Cell().Element(TableHeaderStyle).Text(labels.PriceLabel);
                    header.Cell().Element(TableHeaderStyle).Text(labels.TotalLabel);
                }
            });

            foreach (var item in lineItems)
            {
                if (isRtl)
                {
                    table.Cell().Element(TableCellStyle).AlignRight().Text(FormatMoney(item.Total));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(FormatMoney(item.Price));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(item.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
                    table.Cell().Element(TableCellStyle).AlignRight().Text(item.Description);
                }
                else
                {
                    table.Cell().Element(TableCellStyle).Text(item.Description);
                    table.Cell().Element(TableCellStyle).Text(item.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
                    table.Cell().Element(TableCellStyle).Text(FormatMoney(item.Price));
                    table.Cell().Element(TableCellStyle).Text(FormatMoney(item.Total));
                }
            }
        });
    }

    private static void ComposeTotalsSection(IContainer container, InvoicePdfLabels labels, bool isRtl, decimal subtotal, decimal total)
    {
        container.AlignRight().Width(220).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).Column(col =>
        {
            col.Spacing(6);
            col.Item().Element(x => ComposeTotalRow(x, labels.SubtotalLabel, FormatMoney(subtotal), isRtl, false));
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            col.Item().Element(x => ComposeTotalRow(x, labels.TotalLabel, FormatMoney(total), isRtl, true));
        });
    }

    private static void ComposeTotalRow(IContainer container, string label, string value, bool isRtl, bool emphasize)
    {
        container.Row(row =>
        {
            if (isRtl)
            {
                ApplySemiBold(row.RelativeItem().AlignRight().Text(value), emphasize);
                ApplySemiBold(row.RelativeItem().AlignRight().Text(label), emphasize);
            }
            else
            {
                ApplySemiBold(row.RelativeItem().Text(label), emphasize);
                ApplySemiBold(row.RelativeItem().AlignRight().Text(value), emphasize);
            }
        });
    }

    private static void ComposeNotesSection(IContainer container, InvoicePdfLabels labels, bool isRtl, string notes)
    {
        container.Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(14)
            .Element(x => AlignForDirection(x, isRtl))
            .Column(col =>
            {
                col.Spacing(6);
                col.Item().Text(labels.NotesLabel).SemiBold().FontColor(Colors.Blue.Darken2);
                col.Item().Text(notes);
            });
    }

    private static IContainer TableHeaderStyle(IContainer container)
    {
        return container
            .Background(Colors.Blue.Lighten4)
            .PaddingVertical(8)
            .PaddingHorizontal(6)
            .BorderBottom(1)
            .BorderColor(Colors.Blue.Lighten2);
    }

    private static IContainer TableCellStyle(IContainer container)
    {
        return container
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(8)
            .PaddingHorizontal(6);
    }

    private static string FormatDate(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static IContainer AlignForDirection(IContainer container, bool isRtl)
    {
        return isRtl ? container.AlignRight() : container;
    }

    private static void ApplySemiBold(TextBlockDescriptor descriptor, bool emphasize)
    {
        if (emphasize)
            descriptor.SemiBold();
    }

    private static InvoicePdfLabels GetLabels(string? language)
    {
        var code = (language ?? "en").Trim().ToLowerInvariant();

        return code switch
        {
            "he" or "he-il" => new InvoicePdfLabels(
                true,
                "חשבונית",
                "עסק",
                "פרטי לקוח",
                "פרטי חשבונית",
                "שם לקוח",
                "מספר חשבונית",
                "תאריך חשבונית",
                "תאריך יעד",
                "תיאור",
                "כמות",
                "מחיר",
                "סה\"כ",
                "סכום ביניים",
                "סה\"כ לתשלום",
                "הערות",
                "טלפון",
                "אימייל",
                "פריט שירות",
                "תודה על העסק שלך"),
            "ar" or "ar-sa" or "ar-eg" => new InvoicePdfLabels(
                true,
                "فاتورة",
                "النشاط التجاري",
                "بيانات العميل",
                "بيانات الفاتورة",
                "اسم العميل",
                "رقم الفاتورة",
                "تاريخ الفاتورة",
                "تاريخ الاستحقاق",
                "الوصف",
                "الكمية",
                "السعر",
                "الإجمالي",
                "المجموع الفرعي",
                "الإجمالي الكلي",
                "ملاحظات",
                "الهاتف",
                "البريد الإلكتروني",
                "عنصر خدمة",
                "شكراً لتعاملكم معنا"),
            _ => new InvoicePdfLabels(
                false,
                "INVOICE",
                "Business",
                "Client Details",
                "Invoice Details",
                "Client Name",
                "Invoice Number",
                "Invoice Date",
                "Due Date",
                "Description",
                "Quantity",
                "Price",
                "Total",
                "Subtotal",
                "Total",
                "Notes",
                "Phone",
                "Email",
                "Service Item",
                "Thank you for your business")
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
        string TotalSectionLabel,
        string NotesLabel,
        string PhoneLabel,
        string EmailLabel,
        string DefaultLineItem,
        string Footer);
}