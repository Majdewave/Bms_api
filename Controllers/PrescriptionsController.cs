using Clienta.Api.Data;
using Clienta.Api.Entities;
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

    public PrescriptionsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Prescription request)
    {
        var prescription = new Prescription
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            Date = request.Date,
            Instructions = request.Instructions,
            DoctorName = request.DoctorName,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.Prescriptions.Add(prescription);
        await _context.SaveChangesAsync();

        return Ok(prescription);
    }

    [HttpGet("client/{clientId}")]
    public async Task<IActionResult> GetByClient(Guid clientId)
    {
        var prescriptions = await _context.Prescriptions
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.Date)
            .ToListAsync();

        return Ok(prescriptions);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == id);

        if (prescription == null)
            return NotFound();

        _context.Prescriptions.Remove(prescription);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id)
    {
        var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == id);
        if (prescription == null)
            return NotFound();

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == prescription.ClientId);
        if (client == null)
            return NotFound();

        var idNumber = ExtractPatientId(prescription.Notes);
        var patientName = !string.IsNullOrWhiteSpace(client.FullName) ? client.FullName : string.Empty;
        var patientPhone = client.Phone ?? string.Empty;

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);

                page.Content().Border(1).Padding(15).Column(col =>
                {
                    col.Item().AlignCenter().Text("מרשם רפואי")
                        .FontSize(28)
                        .Bold();

                    col.Item().PaddingVertical(20);

                    col.Item().Border(1).Padding(10).Table(table =>
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
                                c.Item().AlignRight().BorderBottom(1).Text(value ?? "");
                            });
                        }

                        Cell("תאריך", prescription.Date.ToString("yyyy-MM-dd"));
                        Cell("שם המטופל", patientName);

                        Cell("ת.ז", idNumber);
                        Cell("טלפון", patientPhone);
                    });

                    col.Item().PaddingVertical(20);

                    col.Item().Border(1).Padding(10).Column(c =>
                    {
                        c.Item().AlignRight().Text("תרופה:")
                            .Bold()
                            .FontSize(16);

                        c.Item().PaddingTop(10);

                        c.Item().BorderBottom(1).PaddingBottom(5)
                            .AlignRight()
                            .Text("☐ " + prescription.Instructions);
                    });

                    col.Item().PaddingVertical(20);

                    col.Item().Border(1).Padding(10).Column(c =>
                    {
                        c.Item().AlignRight().Text("הוראות שימוש:")
                            .Bold()
                            .FontSize(16);

                        c.Item().PaddingTop(10);

                        c.Item().AlignRight().Text(prescription.Instructions);
                    });

                    col.Item().PaddingVertical(30);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignRight().Text("שם הרופא:");
                            c.Item().BorderBottom(1).PaddingTop(5).Text(prescription.DoctorName);
                        });

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignRight().Text("חתימה:");
                            c.Item().BorderBottom(1).PaddingTop(15).Text("");
                        });
                    });

                    col.Item().PaddingVertical(30);

                    col.Item().AlignCenter()
                        .Text($"מזהה: {prescription.Id}")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken1);
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
        if (match.Success)
            return match.Groups[1].Value;

        return string.Empty;
    }

    private static string ExtractSignature(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "____________________";

        var match = Regex.Match(notes, @"(?:חתימה|signature)\s*[:\-]?\s*([^\r\n]+)", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return "____________________";
    }
}
