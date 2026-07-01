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
    private readonly IFileStorage _fileStorage;

    public TenantController(
        AppDbContext context,
        ITenantContext tenantContext,
        IFileStorage fileStorage)
        
    {
        _context = context;
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
    }

    // GET api/tenant/me
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        // Get new Tenant
        var tenantId = _tenantContext.TenantId;

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        var features = await _context.TenantFeatures
           // .IgnoreQueryFilters()
            .FirstOrDefaultAsync(tf => tf.TenantId == tenantId);

        if (tenant == null)
            return NotFound();

        return Ok(new
        {
            tenant.Name,
            tenant.Phone,
            tenant.WhatsApp,
            tenant.LogoUrl,
            tenant.BusinessStampUrl,
            tenant.AutoDeleteNotDocumentedAfterDays,
            tenant.EnableAutoDeleteNotDocumented,
            tenant.DefaultVatRate,
            tenant.Currency,
            tenant.InvoicePrefix,
            tenant.NextInvoiceNumber,
            beforeAfterPhotosEnabled = features?.BeforeAfterPhotosEnabled ?? true,
            plan = tenant.Plan.ToString(),
            subscriptionStatus = tenant.SubscriptionStatus.ToString(),
            trialEndsAt = tenant.TrialEndsAt,
            SubscriptionEndsAt = tenant.SubscriptionEndsAt,
            isSuspended = tenant.IsSuspended
        });
    }

    // PUT api/tenant/me
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateTenantRequest request)
    {
        var tenantId = _tenantContext.TenantId;

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Name))
            tenant.Name = request.Name;

        tenant.Phone = request.Phone;
        tenant.WhatsApp = request.WhatsApp;

        if (request.DefaultVatRate.HasValue)
            tenant.DefaultVatRate = NormalizeVatRate(request.DefaultVatRate.Value);

        if (request.Currency != null)
            tenant.Currency = NormalizeCurrency(request.Currency);

        if (request.InvoicePrefix != null)
            tenant.InvoicePrefix = NormalizeInvoicePrefix(request.InvoicePrefix);

        if (request.NextInvoiceNumber.HasValue)
            tenant.NextInvoiceNumber = NormalizeNextInvoiceNumber(request.NextInvoiceNumber.Value);

        if (request.LogoUrl != null)
            tenant.LogoUrl = request.LogoUrl;

        if (request.BusinessStampUrl != null)
            tenant.BusinessStampUrl = request.BusinessStampUrl;

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
            tenant.BusinessStampUrl,
            tenant.Plan.ToString(),
            tenant.SubscriptionStatus.ToString(),
            tenant.CreatedAt,
            tenant.AutoDeleteNotDocumentedAfterDays,
            tenant.EnableAutoDeleteNotDocumented,
            tenant.DefaultVatRate,
            tenant.Currency,
            tenant.InvoicePrefix,
            tenant.NextInvoiceNumber
        ));
    }

    // PUT /api/tenant 
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
            tenant.LogoUrl = request.LogoUrl;

        if (request.BusinessStampUrl != null)
            tenant.BusinessStampUrl = request.BusinessStampUrl;

        tenant.Phone = request.Phone;
        tenant.WhatsApp = request.WhatsApp;

        if (request.DefaultVatRate.HasValue)
            tenant.DefaultVatRate = NormalizeVatRate(request.DefaultVatRate.Value);

        if (request.Currency != null)
            tenant.Currency = NormalizeCurrency(request.Currency);

        if (request.InvoicePrefix != null)
            tenant.InvoicePrefix = NormalizeInvoicePrefix(request.InvoicePrefix);

        if (request.NextInvoiceNumber.HasValue)
            tenant.NextInvoiceNumber = NormalizeNextInvoiceNumber(request.NextInvoiceNumber.Value);

        await _context.SaveChangesAsync();

        return Ok(new TenantResponse(
            tenant.Id,
            tenant.Name,
            tenant.Subdomain,
            tenant.LogoUrl,
            tenant.BusinessStampUrl,
            tenant.Plan.ToString(),
            tenant.SubscriptionStatus.ToString(),
            tenant.CreatedAt,
            tenant.AutoDeleteNotDocumentedAfterDays,
            tenant.EnableAutoDeleteNotDocumented,
            tenant.DefaultVatRate,
            tenant.Currency,
            tenant.InvoicePrefix,
            tenant.NextInvoiceNumber
        ));
    }

    [HttpDelete("stamp")]
    public async Task<IActionResult> DeleteStamp()
    {
        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant == null)
            return NotFound();

        tenant.BusinessStampUrl = null;

        await _context.SaveChangesAsync();

        return NoContent();
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
         // .IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantContext.TenantId && !a.IsDocumented)
            .Select(a => a.ClientId)
            .Distinct()
            .ToList();

        var clientsToDelete = _context.Clients
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == _tenantContext.TenantId && clientIdsToDelete.Contains(c.Id)).ToList();

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

        // delete from local path
        //if (!string.IsNullOrEmpty(tenant.LogoUrl))
        //{
        //    var filePath = Path.Combine("wwwroot", tenant.LogoUrl.TrimStart('/'));

        //    if (System.IO.File.Exists(filePath))
        //        System.IO.File.Delete(filePath);
        //}


        // Delete from s3 aws
        if (!string.IsNullOrEmpty(tenant.LogoUrl))
        {
            if (Uri.TryCreate(tenant.LogoUrl, UriKind.Absolute, out var uri))
            {
                var key = uri.AbsolutePath.TrimStart('/');
                await _fileStorage.DeleteAsync(key);
            }
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


            // save local file for dev 
            //using (var stream = new FileStream(filePath, FileMode.Create))
            //{
            //    await file.CopyToAsync(stream);
            //}


            // save in s3 AWS 
            using var stream = file.OpenReadStream();

            var key = $"tenants/{_tenantContext.TenantId}/logo{Path.GetExtension(file.FileName)}";
            var url = await _fileStorage.UploadAsync(stream, key, file.ContentType);
            tenant.LogoUrl = url;


            await _context.SaveChangesAsync();

            return Ok(new TenantResponse(
                tenant.Id,
                tenant.Name,
                tenant.Subdomain,
                tenant.LogoUrl,
                tenant.BusinessStampUrl,
                tenant.Plan.ToString(),
                tenant.SubscriptionStatus.ToString(),
                tenant.CreatedAt,
                tenant.AutoDeleteNotDocumentedAfterDays,
                tenant.EnableAutoDeleteNotDocumented,
                tenant.DefaultVatRate,
                tenant.Currency,
                tenant.InvoicePrefix,
                tenant.NextInvoiceNumber
            ));
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred while uploading the file: {ex.Message}");
        }
    }

    [HttpPost("stamp")]
    public async Task<IActionResult> UploadStamp(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedMimeTypes.Contains(file.ContentType?.ToLower()))
            return BadRequest("Only image files are allowed.");

        if (file.Length > 2 * 1024 * 1024)
            return BadRequest("File size must not exceed 2MB");

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant == null)
            return NotFound();

        using var stream = file.OpenReadStream();

        var key = $"tenants/{_tenantContext.TenantId}/stamp{Path.GetExtension(file.FileName)}";

        var url = await _fileStorage.UploadAsync(stream, key, file.ContentType);

        tenant.BusinessStampUrl = url;

        await _context.SaveChangesAsync();

        return Ok(new TenantResponse(
            tenant.Id,
            tenant.Name,
            tenant.Subdomain,
            tenant.LogoUrl,
            tenant.BusinessStampUrl,
            tenant.Plan.ToString(),
            tenant.SubscriptionStatus.ToString(),
            tenant.CreatedAt,
            tenant.AutoDeleteNotDocumentedAfterDays,
            tenant.EnableAutoDeleteNotDocumented,
            tenant.DefaultVatRate,
            tenant.Currency,
            tenant.InvoicePrefix,
            tenant.NextInvoiceNumber
        ));
    }

    private static decimal NormalizeVatRate(decimal requestedVatRate)
    {
        if (requestedVatRate <= 0 || requestedVatRate > 100)
            return 18m;

        return decimal.Round(requestedVatRate, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCurrency(string requestedCurrency)
    {
        if (string.IsNullOrWhiteSpace(requestedCurrency))
            return "ILS";

        var normalized = requestedCurrency.Trim().ToUpperInvariant();

        return normalized switch
        {
            "ILS" or "USD" or "EUR" => normalized,
            _ => "ILS"
        };
    }

    private static string NormalizeInvoicePrefix(string? invoicePrefix)
    {
        var normalized = invoicePrefix?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "INV-" : normalized;
    }

    private static int NormalizeNextInvoiceNumber(int nextInvoiceNumber)
    {
        return nextInvoiceNumber > 0 ? nextInvoiceNumber : 1;
    }
}