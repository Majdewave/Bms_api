using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.RegularExpressions;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/prescriptions")]
public class PrescriptionsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IFeatureService _featureService;
    private readonly ITenantContext _tenant;

    public PrescriptionsController(AppDbContext context, ITenantContext tenant, IFeatureService featureService)
    {
        _context = context;
        _tenant = tenant;
        _featureService = featureService;
    }

    // ✅ CREATE
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePrescriptionRequest request)
    {
        if (!await _featureService.IsEnabledAsync("prescriptions"))
            return Forbid();

        var prescription = new Prescription
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            Date = request.Date,
            Drugs = request.Drugs ?? new List<string>(),
            Instructions = request.Instructions ?? string.Empty,
            DoctorName = request.DoctorName ?? string.Empty,
            Notes = request.Notes ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _context.Prescriptions.Add(prescription);
        await _context.SaveChangesAsync();

        return Ok(prescription);
    }

    // ✅ GET BY CLIENT
    [HttpGet("client/{clientId}")]
    public async Task<IActionResult> GetByClient(Guid clientId)
    {
        if (!await _featureService.IsEnabledAsync("prescriptions"))
            return Forbid();

        var prescriptions = await _context.Prescriptions
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.Date)
            .ToListAsync();

        return Ok(prescriptions);
    }

    // ✅ DELETE
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _featureService.IsEnabledAsync("prescriptions"))
            return Forbid();

        var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == id);

        if (prescription == null)
            return NotFound();

        _context.Prescriptions.Remove(prescription);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ✅ PDF (עם העיצוב המתוקן)
    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id)
    {
        if (!await _featureService.IsEnabledAsync("prescriptions"))
            return Forbid();

        var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == id);
        if (prescription == null)
            return NotFound();

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == prescription.ClientId);
        if (client == null)
            return NotFound();

        var tenantId = _tenant.TenantId;
        if (tenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);

        var idNumber = client.IdNumber ?? "";
        var patientName = client.FullName ?? "";
        var patientPhone = client.Phone ?? "";
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        var logoUrl = $"http://localhost:5146/uploads/tenants/{tenantId}/logo.png?v={DateTime.UtcNow.Ticks}";

        byte[]? logoBytes = null;
        try
        {
            using var http = new HttpClient();
            logoBytes = await http.GetByteArrayAsync(logoUrl);
        }
        catch { }

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(20);

                page.Content().Column(col =>
                {
                    if (logoBytes != null)
                        col.Item().AlignCenter().Height(110).Image(logoBytes);

                    col.Item().AlignCenter()
                        .Text(tenant?.Name ?? "")
                        .FontSize(18)
                        .Bold();

                    col.Item().AlignCenter().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    var contactText = "";

                    if (!string.IsNullOrWhiteSpace(tenant?.Phone))
                        contactText += tenant.Phone;

                    if (!string.IsNullOrWhiteSpace(tenant?.WhatsApp))
                    {
                        if (!string.IsNullOrEmpty(contactText))
                            contactText += " | ";

                        contactText += $"WhatsApp {tenant.WhatsApp}";
                    }

                    col.Item().AlignCenter()
                        .Text(contactText)
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken1);

                    col.Item().PaddingBottom(5);

                    col.Item().AlignCenter().Text("מרשם רפואי")
                        .FontSize(26)
                        .Bold();

                    col.Item().PaddingVertical(10);

                    col.Item().Border(1).Padding(15).Column(details =>
                    {
                        details.Item().Border(1).Padding(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            void Cell(string label, string value)
                            {
                                table.Cell().Column(c =>
                                {
                                    c.Item().AlignRight().Text(label).FontSize(10).FontColor(Colors.Grey.Darken1);
                                    c.Item().AlignRight().Text(value ?? "").FontSize(12);
                                });
                            }

                            Cell("תאריך", prescription.Date.ToString("yyyy-MM-dd"));
                            Cell("שם המטופל", patientName);
                            Cell("ת.ז", idNumber);
                            Cell("טלפון", patientPhone);
                        });

                        details.Item().PaddingVertical(10);

                        details.Item().Border(1).Padding(10).Column(c =>
                        {
                            c.Item().AlignRight().Text("תרופה").Bold().FontSize(16);
                            c.Item().PaddingTop(10);

                            foreach (var drug in prescription.Drugs)
                            {
                                c.Item()
                                    .BorderBottom(0.3f)
                                    .BorderColor(Colors.Grey.Lighten3)
                                    .PaddingBottom(5)
                                    .Row(r =>
                                    {
                                        r.ConstantItem(20).Text("☐");
                                        r.RelativeItem().AlignRight().Text(drug);
                                    });
                            }
                        });

                        details.Item().PaddingVertical(10);

                        details.Item().Border(1).Padding(10).Column(c =>
                        {
                            c.Item().AlignRight().Text("הוראות שימוש").Bold().FontSize(16);
                            c.Item().PaddingTop(10);
                            c.Item().AlignRight().Text(prescription.Instructions ?? "");
                        });

                        details.Item().PaddingVertical(10);

                        details.Item().Row(row =>
                        {
                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text("שם הרופא").Bold();
                                c.Item().Width(150).Text(prescription.DoctorName);
                            });

                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text("חתימה").Bold();
                                c.Item().Width(150);
                            });
                        });

                        details.Item().PaddingVertical(10);

                        details.Item().AlignCenter()
                            .Text($"הופק בתאריך: {today}")
                            .FontSize(10)
                            .FontColor(Colors.Grey.Darken1);

                        details.Item().PaddingVertical(5);

                        details.Item().AlignCenter()
                            .Text($"מזהה: {prescription.Id}")
                            .FontSize(9)
                            .FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }).GeneratePdf();

        return File(pdf, "application/pdf", $"prescription-{id}.pdf");
    }

    private static string ExtractPatientId(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return string.Empty;

        var match = Regex.Match(notes, @"(?:ת\.?ז\.?|תעודת זהות|ID)\s*[:\-]?\s*([0-9]{5,12})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}