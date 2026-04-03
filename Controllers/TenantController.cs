using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Clienta.Api.DTOs;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/tenant")]
[Authorize]
public class TenantController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IWebHostEnvironment _webHostEnvironment;

    public TenantController(
        AppDbContext context,
        ITenantContext tenantContext,
        IWebHostEnvironment webHostEnvironment)
    {
        _context = context;
        _tenantContext = tenantContext;
        _webHostEnvironment = webHostEnvironment;
    }

    // GET api/tenant/me
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        // TEMP (עד שנחזיר TenantContext)
        var tenantId = Guid.Parse("40AFF269-58DB-4D97-B391-CCBB701CD458");

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
            return NotFound();

        return Ok(new
        {
            tenant.Name,
            tenant.Phone,
            tenant.WhatsApp,
            tenant.LogoUrl,
            tenant.AutoDeleteNotDocumentedAfterDays,
            tenant.EnableAutoDeleteNotDocumented
        });
    }

    // PUT api/tenant/me
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateTenantRequest request)
    {
        var tenantId = Guid.Parse("40AFF269-58DB-4D97-B391-CCBB701CD458");

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Name))
            tenant.Name = request.Name;

        tenant.Phone = request.Phone;
        tenant.WhatsApp = request.WhatsApp;

        await _context.SaveChangesAsync();

        return Ok();
    }

    // GET /api/tenant
    [HttpGet]
    public async Task<IActionResult> GetCurrent()
    {
        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant == null)
            return NotFound();

        return Ok(new TenantResponse(
            tenant.Id,
            tenant.Name,
            tenant.Subdomain,
            tenant.LogoUrl,
            tenant.Plan.ToString(),
            tenant.SubscriptionStatus.ToString(),
            tenant.CreatedAt,
            tenant.AutoDeleteNotDocumentedAfterDays,
            tenant.EnableAutoDeleteNotDocumented
        ));
    }

    // PUT /api/tenant  ✅ FIXED
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateTenantRequest request)
    {
        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Name))
            tenant.Name = request.Name;

        if (request.LogoUrl != null)
            tenant.LogoUrl = request.LogoUrl;   // 👈 חשוב מאוד

        tenant.Phone = request.Phone;
        tenant.WhatsApp = request.WhatsApp;

        await _context.SaveChangesAsync();

        return Ok(new TenantResponse(
            tenant.Id,
            tenant.Name,
            tenant.Subdomain,
            tenant.LogoUrl,
            tenant.Plan.ToString(),
            tenant.SubscriptionStatus.ToString(),
            tenant.CreatedAt,
            tenant.AutoDeleteNotDocumentedAfterDays,
            tenant.EnableAutoDeleteNotDocumented
        ));
    }

    [HttpPut("auto-delete-setting")]
    public async Task<IActionResult> UpdateAutoDeleteSetting([FromBody] AutoDeleteSettingsRequest request)
    {
        var tenantId = _tenantContext.TenantId;

        var tenant = await _context.Tenants.FindAsync(tenantId);

        if (tenant == null)
            return NotFound();

        tenant.AutoDeleteNotDocumentedAfterDays = request.Days;
        tenant.EnableAutoDeleteNotDocumented = request.Enabled;

        await _context.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("run-cleanup")]
    public async Task<IActionResult> RunCleanup()
    {
        var clientIdsToDelete = _context.Appointments
            .IgnoreQueryFilters()
            .Where(a => !a.IsDocumented)
            .Select(a => a.ClientId)
            .Distinct()
            .ToList();

        var clientsToDelete = _context.Clients
            .IgnoreQueryFilters()
            .Where(c => clientIdsToDelete.Contains(c.Id))
            .ToList();

        _context.Clients.RemoveRange(clientsToDelete);

        await _context.SaveChangesAsync();

        return Ok(new { deleted = clientsToDelete.Count });
    }

    [HttpDelete("logo")]
    public async Task<IActionResult> DeleteLogo()
    {
        var tenantId = _tenantContext.TenantId;

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
            return NotFound();

        if (!string.IsNullOrEmpty(tenant.LogoUrl))
        {
            var filePath = Path.Combine("wwwroot", tenant.LogoUrl.TrimStart('/'));

            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }

        tenant.LogoUrl = null;

        await _context.SaveChangesAsync();

        return Ok();
    }

    // POST /api/tenant/logo (לא שיניתי כלום)
    [HttpPost("logo")]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedMimeTypes.Contains(file.ContentType?.ToLower()))
            return BadRequest("Only image files are allowed (JPEG, PNG, GIF, WebP)");

        const long maxFileSize = 2 * 1024 * 1024;
        if (file.Length > maxFileSize)
            return BadRequest("File size must not exceed 2MB");

        try
        {
            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

            if (tenant == null)
                return NotFound();

            var uploadsDir = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "tenants", _tenantContext.TenantId.ToString());
            Directory.CreateDirectory(uploadsDir);

            var fileExtension = Path.GetExtension(file.FileName).ToLower();
            var fileName = $"logo{fileExtension}";
            var filePath = Path.Combine(uploadsDir, fileName);

            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var logoUrl = $"/uploads/tenants/{_tenantContext.TenantId}/{fileName}";
            tenant.LogoUrl = logoUrl;

            await _context.SaveChangesAsync();

            return Ok(new TenantResponse(
                tenant.Id,
                tenant.Name,
                tenant.Subdomain,
                tenant.LogoUrl,
                tenant.Plan.ToString(),
                tenant.SubscriptionStatus.ToString(),
                tenant.CreatedAt,
                tenant.AutoDeleteNotDocumentedAfterDays,
                tenant.EnableAutoDeleteNotDocumented
            ));
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred while uploading the file: {ex.Message}");
        }
    }
}