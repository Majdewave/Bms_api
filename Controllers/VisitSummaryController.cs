using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Clienta.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class VisitSummaryController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IUserDepartmentFeatureAccessService _userDepartmentFeatureAccessService;
        private readonly IDepartmentAccessService _departmentAccessService;

        public VisitSummaryController(
            AppDbContext context,
            IUserDepartmentFeatureAccessService userDepartmentFeatureAccessService,
            IDepartmentAccessService departmentAccessService)
        {
            _context = context;
            _userDepartmentFeatureAccessService = userDepartmentFeatureAccessService;
            _departmentAccessService = departmentAccessService;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] VisitSummary model)
        {
            if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("visitSummariesEnabled"))
                return Forbid();

            if (model == null)
                return BadRequest();

            model.Id = Guid.NewGuid();
            model.CreatedAt = DateTime.UtcNow;

            // אם יש לך TenantMiddleware — הוא כבר שם
            var tenantClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id");
            if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var tenantId))
            {
                model.TenantId = tenantId;

                if (!model.AppointmentId.HasValue || model.AppointmentId == Guid.Empty)
                    return BadRequest("appointmentId is required");

                var accessContext = await _departmentAccessService.GetCurrentUserAccessContextAsync();

                var appointment = await _departmentAccessService.ApplyAppointmentVisibility(
                        _context.Appointments.Where(a =>
                            a.TenantId == tenantId &&
                            a.Id == model.AppointmentId.Value),
                        accessContext)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (appointment == null)
                    return BadRequest("Invalid appointment.");

                if (appointment.ClientId != model.ClientId)
                    return BadRequest("Appointment does not belong to client.");
            }

            // staff מתוך token
            var userId = User.Claims.FirstOrDefault(c => c.Type.Contains("nameidentifier"))?.Value;

            if (Guid.TryParse(userId, out var parsedUserId))
            {
                model.StaffId = parsedUserId;
            }

            _context.VisitSummaries.Add(model);
            await _context.SaveChangesAsync();

            return Ok(model);
        }

        [HttpGet("client/{clientId}")]
        public async Task<IActionResult> GetByClient(Guid clientId)
        {
            if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("visitSummariesEnabled"))
                return Forbid();

            var tenantClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id");

            if (tenantClaim == null || !Guid.TryParse(tenantClaim.Value, out var tenantId))
                return Unauthorized();

            var accessContext = await _departmentAccessService.GetCurrentUserAccessContextAsync();

            var scopedSummaries = _departmentAccessService.ApplyVisitSummaryVisibility(
                _context.VisitSummaries.Where(x => x.ClientId == clientId && x.TenantId == tenantId),
                accessContext);

            var summaries = await scopedSummaries
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            return Ok(summaries);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("visitSummariesEnabled"))
                return Forbid();

            var tenantClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id");

            if (tenantClaim == null || !Guid.TryParse(tenantClaim.Value, out var tenantId))
                return Unauthorized();

            var accessContext = await _departmentAccessService.GetCurrentUserAccessContextAsync();

            var scopedSummaries = _departmentAccessService.ApplyVisitSummaryVisibility(
                _context.VisitSummaries.Where(x => x.Id == id && x.TenantId == tenantId),
                accessContext);

            var summary = await scopedSummaries.FirstOrDefaultAsync();

            if (summary == null)
                return NotFound();

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";

            return Ok(summary);
        }

        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> GetPdf(Guid id)
        {
            if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("visitSummariesEnabled"))
                return Forbid();

            var tenantClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id");

            if (tenantClaim == null || !Guid.TryParse(tenantClaim.Value, out var tenantId))
                return Unauthorized();

            var accessContext = await _departmentAccessService.GetCurrentUserAccessContextAsync();

            var scopedSummaries = _departmentAccessService.ApplyVisitSummaryVisibility(
                _context.VisitSummaries.Where(x => x.Id == id && x.TenantId == tenantId),
                accessContext);

            var summary = await scopedSummaries.FirstOrDefaultAsync();

            if (summary == null)
                return NotFound();

            if (summary == null)
                return NotFound();

            var client = await _context.Clients.FindAsync(summary.ClientId);
            if (client == null)
                return NotFound();

            var businessUser = await _context.BusinessUsers
                .IgnoreQueryFilters()
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.UserId == summary.StaffId);

            byte[]? stampBytes = null;
            if (businessUser?.User?.UseStamp == true &&
                !string.IsNullOrWhiteSpace(businessUser.User.StampUrl))
            {
                try
                {
                    using var http = new HttpClient();
                    stampBytes = await http.GetByteArrayAsync(
                        businessUser.User.StampUrl);
                }
                catch { }
            }

            tenantClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id");

            byte[]? logoBytes = null;

            string businessName = "";
            string? whatsapp = "";

            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant != null)
            {
                businessName = tenant.Name ?? "";
                whatsapp = tenant.Phone ?? "";

                try
                {
                    if (!string.IsNullOrWhiteSpace(tenant.LogoUrl))
                    {
                        using var http = new HttpClient();
                        logoBytes = await http.GetByteArrayAsync(tenant.LogoUrl);
                    }
                }
                catch { }
            }

            var pdfBytes = GenerateVisitSummaryPdf(
                summary,
                client.FullName,
                client.IdNumber,
                client.Phone,
                businessUser?.User?.FullName ?? "",
                businessName,
                whatsapp,
                logoBytes,
                stampBytes
            );
            return File(pdfBytes, "application/pdf", $"VisitSummary_{summary.Id}.pdf");
        }

        private byte[] GenerateVisitSummaryPdf(VisitSummary summary,string clientName,string? idNumber,string? phone, string doctorName,
            string businessName,
            string? whatsapp,
            byte[]? logoBytes,
            byte[]? stampBytes)
        {
            var document = Document.Create(container =>
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
                        // לוגו
                        if (logoBytes != null)
                            col.Item().AlignCenter().Height(100).Image(logoBytes);

                        // שם עסק
                        col.Item().AlignCenter()
                            .Text(businessName)
                            .FontFamily("Noto Sans Hebrew")
                            .FontSize(18)
                            .Bold()
                            .DirectionFromRightToLeft();

                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // פרטי קשר
                        var contactText = "";

                        if (!string.IsNullOrWhiteSpace(phone))
                            contactText += phone;

                        if (!string.IsNullOrWhiteSpace(whatsapp))
                        {
                            if (!string.IsNullOrEmpty(contactText))
                                contactText += " | ";

                            contactText += $"WhatsApp {whatsapp}";
                        }

                        col.Item().AlignCenter()
                            .Text(contactText)
                            .FontFamily("Noto Sans Hebrew")
                            .FontColor(Colors.Grey.Medium)
                            .FontSize(10);

                        col.Item().PaddingBottom(5);

                        // כותרת
                        col.Item().AlignCenter()
                            .Text("סיכום ביקור")
                            .FontFamily("Noto Sans Hebrew")
                            .FontSize(24)
                            .Bold()
                            .DirectionFromRightToLeft();

                        col.Item().PaddingVertical(10);

                        // פרטי מטופל
                        col.Item().Border(1).Padding(10).Column(details =>
                        {
                            void Row(string label, string value)
                            {
                                details.Item().AlignRight().Text($"{label}: {value}")
                                    .FontFamily("Noto Sans Hebrew")
                                    .DirectionFromRightToLeft();
                            }

                            Row("תאריך", summary.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
                            Row("שם מטופל", clientName);
                            Row("ת.ז", idNumber ?? "");
                            Row("טלפון", phone ?? "");
                        });

                        col.Item().PaddingVertical(10);

                        // תוכן
                        void Section(string title, string value)
                        {
                            col.Item().Border(1).Padding(10).Column(c =>
                            {
                                c.Item().AlignRight()
                                    .Text(title)
                                    .FontFamily("Noto Sans Hebrew")
                                    .FontSize(16)
                                    .Bold()
                                    .DirectionFromRightToLeft();

                                c.Item().PaddingTop(5);

                                c.Item().AlignRight()
                                    .Text(value ?? "")
                                    .FontFamily("Noto Sans Hebrew")
                                    .DirectionFromRightToLeft();
                            });
                        }

                        Section("תלונה או ממצא בדיקה", summary.Examination);
                        Section("אבחנה", summary.Diagnosis);
                        Section("המלצות", summary.Recommendations);

                        col.Item().PaddingVertical(20);

                        // חתימה + רופא
                        col.Item().Row(row =>
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

                            //  שם רופא או מטפל
                            row.RelativeItem().AlignCenter().Column(c =>
                            {
                                c.Item().Text("איש צוות מטפל")
                                    .FontFamily("Noto Sans Hebrew")
                                    .Bold()
                                    .DirectionFromRightToLeft();

                                c.Item().Text(doctorName)
                                    .FontFamily("Noto Sans Hebrew")
                                    .DirectionFromRightToLeft();
                            });
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }


        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] VisitSummary updated)
        {
            if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("visitSummariesEnabled"))
                return Forbid();

            var tenantClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id");

            if (tenantClaim == null || !Guid.TryParse(tenantClaim.Value, out var tenantId))
                return Unauthorized();

            var accessContext = await _departmentAccessService.GetCurrentUserAccessContextAsync();

            var scopedSummaries = _departmentAccessService.ApplyVisitSummaryVisibility(
                _context.VisitSummaries.Where(x => x.Id == id && x.TenantId == tenantId),
                accessContext);

            var summary = await scopedSummaries.FirstOrDefaultAsync();

            if (summary == null)
                return NotFound();

            summary.Examination = updated.Examination;
            summary.Diagnosis = updated.Diagnosis;
            summary.Recommendations = updated.Recommendations;

            await _context.SaveChangesAsync();

            return Ok(summary);
        }


        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!await _userDepartmentFeatureAccessService.CanCurrentUserAccessFeatureAsync("visitSummariesEnabled"))
                return Forbid();

            var tenantClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id");

            if (tenantClaim == null || !Guid.TryParse(tenantClaim.Value, out var tenantId))
                return Unauthorized();

            var accessContext = await _departmentAccessService.GetCurrentUserAccessContextAsync();

            var scopedSummaries = _departmentAccessService.ApplyVisitSummaryVisibility(
                _context.VisitSummaries.Where(x => x.Id == id && x.TenantId == tenantId),
                accessContext);

            var summary = await scopedSummaries.FirstOrDefaultAsync();

            if (summary == null)
                return NotFound();

            _context.VisitSummaries.Remove(summary);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
