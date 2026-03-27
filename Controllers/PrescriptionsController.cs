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
    private readonly IWebHostEnvironment _env;

    public PrescriptionsController(AppDbContext context, ITenantContext tenant, IFeatureService featureService, IWebHostEnvironment env)
    {
        _context = context;
        _tenant = tenant;
        _featureService = featureService;
        _env = env;
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
            StaffId = request.StaffId,
            Date = request.Date,
            Drugs = request.Drugs ?? new List<string>(),
            Instructions = request.Instructions ?? string.Empty,
            DoctorName = request.DoctorName ?? string.Empty,
            Notes = request.Notes ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _context.Prescriptions.Add(prescription);
        await _context.SaveChangesAsync();

        User? staff = null;
        if (prescription.StaffId.HasValue)
        {
            // StaffId now contains BusinessUser.Id, so look up User via BusinessUser
            var businessUser = await _context.BusinessUsers
                .Include(bu => bu.User)
                .FirstOrDefaultAsync(bu => bu.Id == prescription.StaffId.Value && bu.TenantId == _tenant.TenantId);
            
            if (businessUser != null)
            {
                staff = businessUser.User;
            }
        }

        return Ok(new PrescriptionResponse
        {
            Id = prescription.Id,
            ClientId = prescription.ClientId,
            StaffId = prescription.StaffId,
            Date = prescription.Date,
            Drugs = prescription.Drugs,
            Instructions = prescription.Instructions,
            DoctorName = prescription.DoctorName,
            Notes = prescription.Notes,
            CreatedAt = prescription.CreatedAt,
            StaffStampUrl = staff?.UseStamp == true ? staff.StampUrl : null
        });
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

        var staffIds = prescriptions
            .Where(p => p.StaffId.HasValue)
            .Select(p => p.StaffId!.Value)
            .Distinct()
            .ToList();

        // ✅ Look up BusinessUsers (StaffId = BusinessUser.Id) and include their User data
        var staffMap = await _context.BusinessUsers
            .Include(bu => bu.User)
            .Where(bu => staffIds.Contains(bu.Id) && bu.TenantId == _tenant.TenantId)
            .ToDictionaryAsync(bu => bu.Id, bu => bu.User);

        var response = prescriptions.Select(p =>
        {
            User? staff = null;
            if (p.StaffId.HasValue)
                staffMap.TryGetValue(p.StaffId.Value, out staff);

            return new PrescriptionResponse
            {
                Id = p.Id,
                ClientId = p.ClientId,
                StaffId = p.StaffId,
                Date = p.Date,
                Drugs = p.Drugs,
                Instructions = p.Instructions,
                DoctorName = p.DoctorName,
                Notes = p.Notes,
                CreatedAt = p.CreatedAt,
                StaffStampUrl = staff?.UseStamp == true ? staff.StampUrl : null
            };
        }).ToList();

        return Ok(response);
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

        byte[]? stampBytes = null;

        if (prescription.StaffId.HasValue)
        {
            // ✅ Look up BusinessUser (StaffId = BusinessUser.Id) and include User data
            var businessUser = await _context.BusinessUsers
                .Include(bu => bu.User)
                .FirstOrDefaultAsync(bu =>
                    bu.Id == prescription.StaffId.Value &&
                    bu.TenantId == _tenant.TenantId);

            var staff = businessUser?.User;

            if (staff?.UseStamp == true && !string.IsNullOrWhiteSpace(staff.StampUrl))
            {
                try
                {
                    var baseUrl = $"{Request.Scheme}://{Request.Host}";
                    var fullUrl = baseUrl + staff.StampUrl;

                    using var http = new HttpClient();
                    stampBytes = await http.GetByteArrayAsync(fullUrl);

                    Console.WriteLine("STAMP LOADED FROM URL ✅");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("STAMP ERROR ❌ " + ex.Message);
                }
            }
        }

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

                        c.Item()
                            .Width(150)
                            .AlignRight()
                            .Text(prescription.DoctorName)
                            .DirectionFromRightToLeft(); // ✅ RTL FIX
                    });

                    row.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item().Text("חתימה").Bold();

                        if (stampBytes != null && stampBytes.Length > 0)
                        {
                            c.Item()
                                .AlignRight()
                                .Width(120)
                                .Height(60) // 👈 קצת יותר גובה (נראה טוב יותר)
                                .Image(stampBytes)
                                .FitArea(); // 👈 הכי חשוב — שלא יימרח
                        }
                        else
                        {
                            c.Item()
                                .AlignRight()
                                .Width(120)
                                .Text("______________");
                        }
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