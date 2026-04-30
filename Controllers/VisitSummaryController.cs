using Clienta.Api.Data;
using Clienta.Api.Entities;
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

        public VisitSummaryController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] VisitSummary model)
        {
            if (model == null)
                return BadRequest();

            model.Id = Guid.NewGuid();
            model.CreatedAt = DateTime.UtcNow;

            // אם יש לך TenantMiddleware — הוא כבר שם
            var tenantClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id");
            if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var tenantId))
            {
                model.TenantId = tenantId;
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
            var tenantClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id");

            if (tenantClaim == null || !Guid.TryParse(tenantClaim.Value, out var tenantId))
                return Unauthorized();

            var summaries = await _context.VisitSummaries
                .Where(x => x.ClientId == clientId && x.TenantId == tenantId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            return Ok(summaries);
        }

        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> GetPdf(Guid id)
        {
            var summary = await _context.VisitSummaries.FindAsync(id);
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
                stampBytes = LoadFileBytesFromUrlOrPath(businessUser.User.StampUrl);
            }

            var pdfBytes = GenerateVisitSummaryPdf(summary, client.FullName, stampBytes);
            return File(pdfBytes, "application/pdf", $"VisitSummary_{summary.Id}.pdf");
        }

        private byte[] GenerateVisitSummaryPdf(VisitSummary summary, string clientName, byte[]? stampBytes)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Content().Column(column =>
                    {
                        column.Item().Text("סיכום ביקור").Bold().FontSize(18);
                        column.Item().Text($"תאריך: {summary.CreatedAt:dd/MM/yyyy}");
                        column.Item().Text($"שם מטופל: {clientName}");
                        column.Item().Text("בדיקה:");
                        column.Item().Text(summary.Examination);
                        column.Item().Text("אבחנה:");
                        column.Item().Text(summary.Diagnosis);
                        column.Item().Text("המלצות:");
                        column.Item().Text(summary.Recommendations);
                        if (stampBytes != null)
                        {
                            column.Item().PaddingTop(20).Image(stampBytes).FitArea();
                        }
                    });
                });
            });
            return document.GeneratePdf();
        }

        private byte[]? LoadFileBytesFromUrlOrPath(string urlOrPath)
        {
            // Implement logic to load bytes from a URL or local path
            // For now, only local file system is supported
            if (System.IO.File.Exists(urlOrPath))
                return System.IO.File.ReadAllBytes(urlOrPath);
            // Optionally, add logic for URLs
            return null;
        }
    }
}
