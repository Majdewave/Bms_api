using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/consents")]
[Authorize(Policy = "manage_appointments")]
public class ConsentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IWebHostEnvironment _env;

    public ConsentsController(AppDbContext context, ITenantContext tenant, IWebHostEnvironment env)
    {
        _context = context;
        _tenant = tenant;
        _env = env;
    }

    [AllowAnonymous]
    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] ConsentTemplateRequest request)
    {
        if (request == null || request.ServiceId == Guid.Empty)
            return BadRequest("serviceId is required");

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("content is required");

        var serviceExists = await _context.Services.AnyAsync(s => s.Id == request.ServiceId);
        if (!serviceExists)
            return NotFound("Service not found");

        var normalizedTemplateContent = NormalizeTemplateToHebrewOnly(request.Content);

        var template = new ConsentTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ServiceId = request.ServiceId,
            Name = request.Name ?? request.Title ?? string.Empty,
            Content = normalizedTemplateContent,
            CreatedAt = DateTime.UtcNow
        };

        _context.ConsentTemplates.Add(template);
        await _context.SaveChangesAsync();

        Console.WriteLine("POST TEMPLATE SERVICE ID REQUEST: " + request.ServiceId);
        Console.WriteLine("POST TEMPLATE SERVICE ID SAVED: " + template.ServiceId);

        return Ok(new
        {
            id = template.Id,
            serviceId = template.ServiceId,
            name = template.Name,
            content = template.Content,
            createdAt = template.CreatedAt
        });
    }

    [AllowAnonymous]
    [HttpGet("templates/service/{serviceId}")]
    public async Task<IActionResult> GetTemplateByService(Guid serviceId)
    {
        Console.WriteLine("SERVICE ID REQUEST: " + serviceId);

        var allTemplates = await _context.ConsentTemplates
            .IgnoreQueryFilters()
            .ToListAsync();

        foreach (var t in allTemplates)
        {
            Console.WriteLine($"DB TEMPLATE: {t.Id} | ServiceId: {t.ServiceId}");
        }

        var template = await _context.ConsentTemplates
            .IgnoreQueryFilters()
            .Where(t => t.ServiceId == serviceId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                id = t.Id,
                serviceId = t.ServiceId,
                content = t.Content,
                name = t.Name,
                createdAt = t.CreatedAt
            })
            .FirstOrDefaultAsync();

        return Ok(template);
    }

    [HttpPost("sign")]
    public async Task<IActionResult> SignConsent([FromBody] SignConsentRequest request)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (string.IsNullOrWhiteSpace(request.ConsentContent))
            return BadRequest("consentContent is required");

        if (request.ClientId == Guid.Empty)
            return BadRequest("clientId is required");

        if (!request.AppointmentId.HasValue || request.AppointmentId.Value == Guid.Empty)
            return BadRequest("appointmentId is required");

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId);
        if (client == null)
            return NotFound("Client not found");

        var appointment = await _context.Appointments
            .Include(a => a.Service)
            .FirstOrDefaultAsync(a => a.Id == request.AppointmentId.Value);

        if (appointment == null)
            return NotFound("Appointment not found");

        ConsentTemplate? template = null;
        if (request.TemplateId != Guid.Empty)
        {
            template = await _context.ConsentTemplates
                .FirstOrDefaultAsync(t => t.Id == request.TemplateId);
        }

        Service? service = null;
        if (appointment?.ServiceId.HasValue == true)
        {
            service = appointment.Service;
        }
        else if (request.ServiceId.HasValue && request.ServiceId.Value != Guid.Empty)
        {
            service = await _context.Services.FirstOrDefaultAsync(s => s.Id == request.ServiceId.Value);
        }

        var clientSignatureUrl = SaveClientSignatureFromBase64(request.ClientSignatureBase64, request.ClientId);

        var consent = new ClientConsent
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ClientId = request.ClientId,
            AppointmentId = appointment.Id,
            TemplateId = request.TemplateId == Guid.Empty ? null : request.TemplateId,
            ServiceId = service?.Id,
            ConsentContent = ReplaceConsentPlaceholders(request.ConsentContent, service?.Name),
            ClientSignatureUrl = clientSignatureUrl,
            SignedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _context.ClientConsents.Add(consent);
        await _context.SaveChangesAsync();

        return Ok(new ConsentResponse
        {
            Id = consent.Id,
            ClientId = consent.ClientId,
            AppointmentId = consent.AppointmentId,
            ConsentContent = consent.ConsentContent,
            ClientSignatureUrl = consent.ClientSignatureUrl,
            SignedAt = consent.SignedAt,
            CreatedAt = consent.CreatedAt,
            TemplateName = template?.Name,
            ServiceName = service?.Name
        });
    }

    [HttpGet("client/{clientId}")]
    public async Task<IActionResult> GetByClient(Guid clientId)
    {
        var consents = await _context.ClientConsents
            .Include(c => c.Appointment)
                .ThenInclude(a => a.Service)
            .Include(c => c.Appointment)
                .ThenInclude(a => a.Staff)
                    .ThenInclude(s => s.User)
            .Where(c => c.ClientId == clientId)
            .OrderByDescending(c => c.SignedAt)
            .Select(c => new ConsentResponse
            {
                Id = c.Id,
                ClientId = c.ClientId,
                AppointmentId = c.AppointmentId,
                ConsentContent = c.ConsentContent,
                ClientSignatureUrl = c.ClientSignatureUrl,
                DoctorSignatureUrl = c.Appointment != null
                    && c.Appointment.Staff != null
                    && c.Appointment.Staff.User != null
                    && c.Appointment.Staff.User.UseStamp
                    ? c.Appointment.Staff.User.StampUrl
                    : null,
                SignedAt = c.SignedAt,
                CreatedAt = c.CreatedAt,
                ServiceName = c.Appointment != null && c.Appointment.Service != null
                    ? c.Appointment.Service.Name
                    : null,
                TemplateName = _context.ConsentTemplates
                    .Where(t => t.ServiceId == c.Appointment.ServiceId)
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => t.Name)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(consents);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var consent = await _context.ClientConsents
            .Include(c => c.Appointment)
                .ThenInclude(a => a.Service)
            .Include(c => c.Appointment)
                .ThenInclude(a => a.Staff)
                    .ThenInclude(s => s.User)
            .Where(c => c.Id == id)
            .Select(c => new ConsentResponse
            {
                Id = c.Id,
                ClientId = c.ClientId,
                AppointmentId = c.AppointmentId,
                ConsentContent = c.ConsentContent,
                ClientSignatureUrl = c.ClientSignatureUrl,
                DoctorSignatureUrl = c.Appointment != null
                    && c.Appointment.Staff != null
                    && c.Appointment.Staff.User != null
                    && c.Appointment.Staff.User.UseStamp
                    ? c.Appointment.Staff.User.StampUrl
                    : null,
                SignedAt = c.SignedAt,
                CreatedAt = c.CreatedAt,
                ServiceName = c.Appointment != null && c.Appointment.Service != null
                    ? c.Appointment.Service.Name
                    : null,
                TemplateName = _context.ConsentTemplates
                    .Where(t => t.ServiceId == c.Appointment.ServiceId)
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => t.Name)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (consent == null)
            return NotFound();

        return Ok(consent);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var consent = await _context.ClientConsents.FindAsync(id);
        if (consent == null)
            return NotFound();

        _context.ClientConsents.Remove(consent);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetSignedConsentPdf(Guid id)
    {
        var consent = await _context.ClientConsents.FirstOrDefaultAsync(c => c.Id == id);
        if (consent == null)
            return NotFound();

        var appointment = await _context.Appointments
            .Include(a => a.Service)
            .FirstOrDefaultAsync(a => a.Id == consent.AppointmentId);

        if (appointment == null)
            return NotFound("Appointment not found");

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == consent.ClientId);
        if (client == null)
            return NotFound("Client not found");

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId);

        User? doctor = null;
        if (appointment.StaffId.HasValue)
        {
            var businessUser = await _context.BusinessUsers
                .Include(bu => bu.User)
                .FirstOrDefaultAsync(bu => bu.Id == appointment.StaffId.Value && bu.TenantId == _tenant.TenantId);

            doctor = businessUser?.User;
        }

        byte[]? logoBytes = LoadFileBytesFromUrlOrPath(tenant?.LogoUrl);
        byte[]? clientSignatureBytes = LoadFileBytesFromUrlOrPath(consent.ClientSignatureUrl);
        byte[]? doctorStampBytes = null;

        if (doctor?.UseStamp == true && !string.IsNullOrWhiteSpace(doctor.StampUrl))
            doctorStampBytes = LoadFileBytesFromUrlOrPath(doctor.StampUrl);

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(20);

                page.Content().Column(col =>
                {
                    if (logoBytes != null)
                        col.Item().AlignCenter().Height(90).Image(logoBytes);

                    col.Item().AlignCenter().Text(tenant?.Name ?? string.Empty).FontSize(16).Bold();
                    col.Item().AlignCenter().Text("טופס הסכמה חתום").FontSize(24).Bold();

                    col.Item().PaddingTop(15).Border(1).Padding(12).Column(section =>
                    {
                        section.Item().AlignRight().Text($"תאריך חתימה: {consent.SignedAt:yyyy-MM-dd HH:mm}");
                        section.Item().AlignRight().Text($"שם מטופל: {client.FullName}");
                        section.Item().AlignRight().Text($"שירות: {appointment.Service?.Name ?? string.Empty}");
                        section.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        section.Item().PaddingTop(10).AlignRight().Column(col =>
                        {
                            col.Spacing(8);

                            // DEBUG: Log raw database content
                            Console.WriteLine("=== RAW CONSENT CONTENT FROM DB ===");
                            Console.WriteLine(consent.ConsentContent);
                            Console.WriteLine("=== END CONTENT ===");

                            var content = (consent.ConsentContent ?? string.Empty)
                                .Replace("&quot;", "\"")
                                .Replace("&nbsp;", " ")
                                .Replace('\u00A0', ' ');

                            content = NormalizeConsentStructure(
                                content,
                                client.FullName,
                                appointment.Service?.Name ?? string.Empty,
                                consent.SignedAt);

                            if (content.Contains("<h2>"))
                            {
                                var title = ExtractBetween(content, "<h2>", "</h2>");
                                var fixedTitle = FixMixedText(title).Trim();
                                if (!string.IsNullOrWhiteSpace(fixedTitle))
                                {
                                    col.Item().AlignRight().Text(fixedTitle)
                                        .FontSize(18)
                                        .Bold()
                                        .DirectionFromRightToLeft()
                                        .AlignRight();
                                }
                            }

                            var paragraphs = ExtractAll(content, "<p>", "</p>");
                            if (paragraphs.Count == 0 && !string.IsNullOrWhiteSpace(content))
                                paragraphs.Add(content);

                            foreach (var p in paragraphs)
                            {
                                var parts = SplitByStrongTags(p);
                                col.Item().AlignRight().Text(text =>
                                {
                                    foreach (var part in parts)
                                    {
                                        var fixedText = FixMixedText(part.text
                                            .Replace("&quot;", "\"")
                                            .Replace("&nbsp;", " ")
                                            .Replace('\u00A0', ' '));

                                        if (string.IsNullOrWhiteSpace(fixedText))
                                            continue;

                                        var span = text.Span(fixedText).FontSize(13).DirectionFromRightToLeft();
                                        if (part.isBold)
                                            span.Bold();
                                    }
                                });
                            }
                        });
                    });

                    col.Item().PaddingTop(20).Row(row =>
                    {
                        row.RelativeItem().AlignCenter().Column(sig =>
                        {
                            sig.Spacing(4);
                            sig.Item().AlignCenter().Text("חתימת מטופל").Bold();
                            if (clientSignatureBytes != null)
                            {
                                sig.Item().AlignCenter().Width(120).Height(50).Image(clientSignatureBytes).FitArea();
                            }
                            else
                            {
                                sig.Item().AlignCenter().Width(120).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            }
                            sig.Item().PaddingTop(6).AlignCenter().Text(client.FullName).FontSize(10);
                        });

                        row.RelativeItem().AlignCenter().Column(sig =>
                        {
                            sig.Spacing(6);
                            sig.Item().AlignCenter().Text("חותמת רופא").Bold();
                            if (doctorStampBytes != null)
                            {
                                sig.Item().AlignCenter().Width(120).Height(50).Image(doctorStampBytes).FitArea();
                            }
                            else
                            {
                                sig.Item().AlignCenter().Width(120).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            }

                            sig.Item().PaddingTop(6).AlignCenter().Text(doctor?.FullName ?? string.Empty).FontSize(11);
                        });
                    });
                });
            });
        }).GeneratePdf();

        return File(pdf, "application/pdf", $"consent-{id}.pdf");
    }

    private static ConsentResponse ToResponse(ClientConsent consent) => new()
    {
        Id = consent.Id,
        ClientId = consent.ClientId,
        AppointmentId = consent.AppointmentId,
        ConsentContent = consent.ConsentContent,
        ClientSignatureUrl = consent.ClientSignatureUrl,
        SignedAt = consent.SignedAt,
        CreatedAt = consent.CreatedAt
    };

    private static string ReplaceConsentPlaceholders(string template, string? serviceName)
    {
        return template.Replace("{{serviceName}}", serviceName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractBetween(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start);
        var endIndex = text.IndexOf(end);

        if (startIndex == -1 || endIndex == -1)
            return string.Empty;

        startIndex += start.Length;
        return text.Substring(startIndex, endIndex - startIndex);
    }

    private static List<string> ExtractAll(string text, string start, string end)
    {
        var result = new List<string>();
        int index = 0;

        while (true)
        {
            var startIndex = text.IndexOf(start, index);
            if (startIndex == -1) break;

            var endIndex = text.IndexOf(end, startIndex);
            if (endIndex == -1) break;

            startIndex += start.Length;
            result.Add(text.Substring(startIndex, endIndex - startIndex));

            index = endIndex + end.Length;
        }

        return result;
    }

    private static List<(string text, bool isBold)> SplitByStrongTags(string input)
    {
        var result = new List<(string text, bool isBold)>();
        var index = 0;

        while (index < input.Length)
        {
            var start = input.IndexOf("<strong>", index, StringComparison.OrdinalIgnoreCase);

            if (start == -1)
            {
                result.Add((input[index..], false));
                break;
            }

            if (start > index)
                result.Add((input.Substring(index, start - index), false));

            var end = input.IndexOf("</strong>", start, StringComparison.OrdinalIgnoreCase);
            if (end == -1)
            {
                result.Add((input[start..], false));
                break;
            }

            var boldStart = start + "<strong>".Length;
            result.Add((input.Substring(boldStart, end - boldStart), true));

            index = end + "</strong>".Length;
        }

        return result;
    }

    private static string NormalizeConsentStructure(string input, string clientName, string serviceName, DateTime date)
    {
        var hebrewSentence = $"הנני {clientName} נותן/ת הסכמתי לקבל טיפול {serviceName} בתאריך {date:yyyy-MM-dd}";

        return input
            .Replace("I, {{clientName}}, consent to receive treatment {{serviceName}} on {{date}}", hebrewSentence, StringComparison.OrdinalIgnoreCase)
            .Replace("I, {{clientName}}, consent to receive...", hebrewSentence, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTemplateToHebrewOnly(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var hebrewTemplate = "הנני {{clientName}} נותן/ת הסכמתי לקבל טיפול {{serviceName}} בתאריך {{date}}";

        return input
            .Replace("&quot;", "\"")
            .Replace("&nbsp;", " ")
            .Replace('\u00A0', ' ')
            .Replace("I, {{clientName}}, consent to receive treatment {{serviceName}} on {{date}}", hebrewTemplate, StringComparison.OrdinalIgnoreCase)
            .Replace("I, {{clientName}}, consent to receive...", hebrewTemplate, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string FixMixedText(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var RLM = "\u200F";

        return RLM + input
            .Replace("IV", RLM + "IV")
            .Replace(":", ":" + RLM)
            .Replace("/", "/" + RLM)
            .Replace(",", "," + RLM)
            .Replace(".", "." + RLM);
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

    private string? SaveClientSignatureFromBase64(string? clientSignatureBase64, Guid clientId)
    {
        if (string.IsNullOrWhiteSpace(clientSignatureBase64))
            return null;

        try
        {
            var raw = clientSignatureBase64.Trim();
            var commaIndex = raw.IndexOf(',');
            if (commaIndex >= 0)
                raw = raw[(commaIndex + 1)..];

            var bytes = Convert.FromBase64String(raw);

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var relativeDir = Path.Combine("uploads", "tenants", _tenant.TenantId.ToString(), "consents", clientId.ToString());
            var absoluteDir = Path.Combine(webRoot, relativeDir);
            Directory.CreateDirectory(absoluteDir);

            var fileName = $"client-signature-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png";
            var absoluteFilePath = Path.Combine(absoluteDir, fileName);
            System.IO.File.WriteAllBytes(absoluteFilePath, bytes);

            return "/" + Path.Combine(relativeDir, fileName).Replace('\\', '/');
        }
        catch
        {
            return null;
        }
    }
}
