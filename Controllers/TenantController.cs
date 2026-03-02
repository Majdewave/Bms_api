using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.DTOs;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
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
            tenant.CreatedAt
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

        tenant.Name = request.Name;
        tenant.LogoUrl = request.LogoUrl;   // 👈 חשוב מאוד

        await _context.SaveChangesAsync();

        return Ok(new TenantResponse(
            tenant.Id,
            tenant.Name,
            tenant.Subdomain,
            tenant.LogoUrl,
            tenant.Plan.ToString(),
            tenant.SubscriptionStatus.ToString(),
            tenant.CreatedAt
        ));
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
                tenant.CreatedAt
            ));
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred while uploading the file: {ex.Message}");
        }
    }
}