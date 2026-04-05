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

        var template = new ConsentTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ServiceId = request.ServiceId,
            Name = request.Name ?? request.Title ?? string.Empty,
            Content = request.Content,
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
            ConsentContent = ReplaceConsentPlaceholders(request.ConsentContent, service?.Name),
            ClientSignatureUrl = clientSignatureUrl,
            SignedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _context.ClientConsents.Add(consent);
        await _context.SaveChangesAsync();

        return Ok(ToResponse(consent));
    }

    [HttpGet("client/{clientId}")]
    public async Task<IActionResult> GetByClient(Guid clientId)
    {
        var consents = await _context.ClientConsents
            .Where(c => c.ClientId == clientId)
            .OrderByDescending(c => c.SignedAt)
            .ToListAsync();

        var response = consents.Select(ToResponse).ToList();
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var consent = await _context.ClientConsents.FirstOrDefaultAsync(c => c.Id == id);
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
                        section.Item().PaddingTop(10).AlignRight().Text(consent.ConsentContent).FontSize(12);
                    });

                    col.Item().PaddingTop(20).Row(row =>
                    {
                        row.RelativeItem().AlignRight().Column(sig =>
                        {
                            sig.Item().AlignRight().Text("חתימת מטופל").Bold();
                            if (clientSignatureBytes != null)
                            {
                                sig.Item().AlignRight().Width(150).Height(70).Image(clientSignatureBytes).FitArea();
                            }
                            else
                            {
                                sig.Item().AlignRight().Width(150).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            }
                        });

                        row.RelativeItem().AlignRight().Column(sig =>
                        {
                            sig.Item().AlignRight().Text("חותמת רופא").Bold();
                            if (doctorStampBytes != null)
                            {
                                sig.Item().AlignRight().Width(150).Height(70).Image(doctorStampBytes).FitArea();
                            }
                            else
                            {
                                sig.Item().AlignRight().Width(150).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            }

                            sig.Item().PaddingTop(6).AlignRight().Text(doctor?.FullName ?? string.Empty).FontSize(11);
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
