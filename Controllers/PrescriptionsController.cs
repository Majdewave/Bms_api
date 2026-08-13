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
    private readonly IUserDepartmentFeatureAccessService _userDepartmentFeatureAccessService;
    private readonly ITenantContext _tenant;
    private readonly IWebHostEnvironment _env;

    public PrescriptionsController(
        AppDbContext context,
        ITenantContext tenant,
        IUserDepartmentFeatureAccessService userDepartmentFeatureAccessService,
        IWebHostEnvironment env)
    {
        _context = context;
        _tenant = tenant;
        _userDepartmentFeatureAccessService = userDepartmentFeatureAccessService;
        _env = env;
    }

    // ✅ CREATE
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePrescriptionRequest request)
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("prescriptionsEnabled"))
            return Forbid();

        var prescription = new Prescription
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            StaffId = request.StaffId,
            Date = request.Date.ToUniversalTime(),
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
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("prescriptionsEnabled"))
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

    // ✅ GET BY ID
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("prescriptionsEnabled"))
            return Forbid();

        var prescription = await _context.Prescriptions
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prescription == null)
            return NotFound();

        User? staff = null;
        if (prescription.StaffId.HasValue)
        {
            var businessUser = await _context.BusinessUsers
                .Include(bu => bu.User)
                .FirstOrDefaultAsync(bu => bu.Id == prescription.StaffId.Value && bu.TenantId == _tenant.TenantId);

            staff = businessUser?.User;
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

    // ✅ UPDATE
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePrescriptionRequest request)
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("prescriptionsEnabled"))
            return Forbid();

        var prescription = await _context.Prescriptions
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prescription == null)
            return NotFound();

        prescription.ClientId = request.ClientId;
        prescription.StaffId = request.StaffId;
        prescription.Date = request.Date.ToUniversalTime();
        prescription.Drugs = request.Drugs ?? new List<string>();
        prescription.Instructions = request.Instructions ?? string.Empty;
        prescription.DoctorName = request.DoctorName ?? string.Empty;
        prescription.Notes = request.Notes ?? string.Empty;

        await _context.SaveChangesAsync();

        User? staff = null;
        if (prescription.StaffId.HasValue)
        {
            var businessUser = await _context.BusinessUsers
                .Include(bu => bu.User)
                .FirstOrDefaultAsync(bu => bu.Id == prescription.StaffId.Value && bu.TenantId == _tenant.TenantId);

            staff = businessUser?.User;
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

    // ✅ DELETE
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("prescriptionsEnabled"))
            return Forbid();

        var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == id);

        if (prescription == null)
            return NotFound();

        _context.Prescriptions.Remove(prescription);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ✅ PDF 
    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id)
    {
        if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("prescriptionsEnabled"))
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


        var logoUrl = tenant?.LogoUrl;

        byte[]? logoBytes = null;
        if (!string.IsNullOrWhiteSpace(logoUrl))
        {
            try
            {
                using var http = new HttpClient();
                logoBytes = await http.GetByteArrayAsync(logoUrl);
            }
            catch { }
        }

        byte[]? stampBytes = null;

        User? staff = null;

        // מביא staff בלי תלות ב-Tenant
        if (prescription.StaffId.HasValue)
        {
            var businessUser = await _context.BusinessUsers
               .IgnoreQueryFilters()
                .Include(bu => bu.User)
                .FirstOrDefaultAsync(bu => bu.UserId == prescription.StaffId);

               staff = businessUser?.User;

            Console.WriteLine("StaffId: " + prescription.StaffId);
            Console.WriteLine("TenantId: " + _tenant.TenantId);
            Console.WriteLine("Found BU: " + (businessUser != null));
            Console.WriteLine("UseStamp: " + businessUser?.User?.UseStamp);
            Console.WriteLine("StampUrl: " + businessUser?.User?.StampUrl);

        }



        //  fallback אם אין חותמת
        // אם אין חותמת — פשוט לא מציגים
        if (staff == null || !staff.UseStamp || string.IsNullOrWhiteSpace(staff.StampUrl))
        {
            stampBytes = null;
        }
        //  טעינת חותמת
        if (staff?.UseStamp == true && !string.IsNullOrWhiteSpace(staff.StampUrl))
        {
            try
            {
                using var http = new HttpClient();
                stampBytes = await http.GetByteArrayAsync(staff.StampUrl);
            }
            catch { }
        }

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(20);
                // page number
                page.Footer().Row(row =>
                {
                    row.RelativeItem()
                        .AlignLeft()
                        .Text($"הופק: {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .FontFamily("Noto Sans Hebrew")
                        .FontSize(8)
                        .FontColor(Colors.Grey.Medium);

                    row.RelativeItem()
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.Span("עמוד ").FontSize(9).FontFamily("Noto Sans Hebrew").FontColor(Colors.Grey.Medium);
                            text.CurrentPageNumber().FontSize(9).FontFamily("Noto Sans Hebrew").FontColor(Colors.Grey.Medium);
                            text.Span(" מתוך ").FontSize(9).FontFamily("Noto Sans Hebrew").FontColor(Colors.Grey.Medium);
                            text.TotalPages().FontSize(9).FontFamily("Noto Sans Hebrew").FontColor(Colors.Grey.Medium);
                        });
                    row.RelativeItem()
                        .AlignRight()
                        .Text("CLIENTA")
                        .FontSize(8)
                        .FontColor(Colors.Grey.Medium);
                });

                page.Content().Column(col =>
                {
                    //  לוגו
                    if (logoBytes != null)
                        col.Item().AlignCenter().Height(110).Image(logoBytes);

                    //  שם עסק
                    col.Item().AlignCenter()
                        .Text(tenant?.Name ?? "")
                        .FontFamily("Noto Sans Hebrew")
                        .FontSize(18)
                        .Bold()
                        .DirectionFromRightToLeft();

                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    // פרטי קשר
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
                        .FontFamily("Noto Sans Hebrew")
                        .FontColor(Colors.Grey.Medium)
                        .FontSize(10);

                    col.Item().PaddingBottom(5);

                    // 🔹 כותרת
                    col.Item().AlignCenter()
                        .Text("מרשם רפואי")
                        .FontFamily("Noto Sans Hebrew")
                        .FontSize(26)
                        .Bold()
                        .DirectionFromRightToLeft();

                    col.Item().PaddingVertical(10);

                    col.Item().Border(1).Padding(15).Column(details =>
                    {
                        // 🔹 פרטי מטופל
                        details.Item().Table(table =>
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
                                    c.Item().AlignRight()
                                        .Text(label)
                                        .FontFamily("Noto Sans Hebrew")
                                        .FontSize(10)
                                        .Bold(); 

                                    
                                    c.Item().AlignRight()
                                        .Text(value ?? "")
                                        .FontFamily("Noto Sans Hebrew")
                                        .FontSize(12)
                                        .DirectionFromRightToLeft();
                                });
                            }

                            Cell("תאריך", prescription.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
                            Cell("שם המטופל", patientName);
                            Cell("ת.ז", idNumber);
                            Cell("טלפון", patientPhone);
                        });

                        details.Item().PaddingVertical(10);

                        //  תרופות
                        details.Item().Border(1).Padding(10).Column(c =>
                        {
                            c.Item().AlignRight()
                                .Text("תרופה")
                                .FontFamily("Noto Sans Hebrew")
                                .FontSize(16)
                                .Bold();

                            c.Item().PaddingTop(10);

                            foreach (var drug in prescription.Drugs)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(20).Text("[  ]"); 
                                    r.RelativeItem().AlignRight()
                                        .Text(drug)
                                        .FontFamily("Noto Sans Hebrew")
                                        .DirectionFromRightToLeft();
                                });
                            }
                        });

                        details.Item().PaddingVertical(10);

                        // הוראות
                        details.Item().Border(1).Padding(10).Column(c =>
                        {
                            c.Item().AlignRight()
                                .Text("הוראות שימוש")
                                .FontFamily("Noto Sans Hebrew")
                                .FontSize(16)
                                .Bold()
                                .DirectionFromRightToLeft();

                            c.Item().PaddingTop(10);

                            c.Item().AlignRight()
                                .Text(prescription.Instructions ?? "" )
                                .FontFamily("Noto Sans Hebrew")
                                .DirectionFromRightToLeft();
                             });

                        details.Item().PaddingVertical(10);

                        //  חתימה + רופא
                        details.Item().Row(row =>
                        {
                            row.Spacing(40);
                            // חתימה
                            row.RelativeItem().AlignCenter().Column(c =>
                            {
                                c.Item().AlignCenter().Text("חתימה")
                                    .FontFamily("Noto Sans Hebrew")
                                    .Bold()
                                    .DirectionFromRightToLeft();

                                if (stampBytes != null && stampBytes.Length > 0)
                                {
                                    c.Item()
                                        .Width(120)
                                        .Height(60)
                                        .Image(stampBytes); 
                                }
                                else
                                {
                                    c.Item()
                                        .Width(120)
                                        .AlignCenter()
                                        .LineHorizontal(1);
                                }
                            });

                            // שם רופא או מטפל
                            row.RelativeItem().AlignCenter().Column(c =>
                            {
                                c.Item().AlignCenter().Text("איש צוות מטפל")
                                    .FontFamily("Noto Sans Hebrew")
                                    .Bold()
                                    .DirectionFromRightToLeft();

                                c.Item()
                                    .Text(prescription.DoctorName ?? "")
                                    .FontFamily("Noto Sans Hebrew")
                                    .DirectionFromRightToLeft()
                                    .FontSize(11);
                            });
                        });

                        details.Item().PaddingVertical(10);

                        var producedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                        details.Item().AlignCenter()
                            .Text($"הופק בתאריך: {producedAt}")
                            .FontFamily("Noto Sans Hebrew")
                            .FontSize(10);

                        details.Item().AlignCenter()
                            .Text($"מזהה: {prescription.Id}")
                            .FontFamily("Noto Sans Hebrew")
                            .FontSize(9);
                    });
                });
            });
        }).GeneratePdf();

        return File(pdf, "application/pdf", $"prescription-{id}.pdf");
    }

    private byte[]? LoadFileBytesFromUrlOrPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        var normalized = rawPath.Trim();

        if (Path.IsPathRooted(normalized) && System.IO.File.Exists(normalized))
            return System.IO.File.ReadAllBytes(normalized);

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute))
            normalized = absolute.LocalPath;

        normalized = normalized.TrimStart('~').TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        var webRootPath = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var fullPath = Path.Combine(webRootPath, normalized);

        if (!System.IO.File.Exists(fullPath))
            return null;

        return System.IO.File.ReadAllBytes(fullPath);
    }

    private static string ExtractPatientId(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return string.Empty;

        var match = Regex.Match(notes, @"(?:ת\.?ז\.?|תעודת זהות|ID)\s*[:\-]?\s*([0-9]{5,12})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}