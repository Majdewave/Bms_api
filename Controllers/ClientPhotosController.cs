using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/client-photos")]
[Authorize(Policy = "manage_clients")]
public class ClientPhotosController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IWebHostEnvironment _env;
    private readonly IFeatureService _featureService;

    private const long MaxImageSizeBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public ClientPhotosController(AppDbContext context, ITenantContext tenant, IWebHostEnvironment env, IFeatureService featureService)
    {
        _context = context;
        _tenant = tenant;
        _env = env;
        _featureService = featureService;
    }

    [HttpPost]
    public async Task<IActionResult> Upload([FromForm] CreateClientTreatmentPhotoRequest request)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var features = await _featureService.GetAsync();
        if (!features.BeforeAfterPhotosEnabled)
            return BadRequest("Before/After photos feature is disabled.");

        if (request.ClientId == Guid.Empty)
            return BadRequest("clientId is required");

        if (request.BeforeImage == null && request.AfterImage == null)
            return BadRequest("At least one of beforeImage or afterImage is required.");

        var clientExists = await _context.Clients.AnyAsync(c => c.Id == request.ClientId);
        if (!clientExists)
            return NotFound("Client not found.");

        var validationError = ValidateImage(request.BeforeImage, "beforeImage")
            ?? ValidateImage(request.AfterImage, "afterImage");
        if (validationError != null)
            return BadRequest(validationError);

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var relativeDirectory = Path.Combine("uploads", "tenants", _tenant.TenantId.ToString(), "clients", request.ClientId.ToString(), "photos");
        var absoluteDirectory = Path.Combine(webRoot, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var beforeUrl = request.BeforeImage != null
            ? await SaveImageAsync(request.BeforeImage, absoluteDirectory, relativeDirectory, "before")
            : null;
        var afterUrl = request.AfterImage != null
            ? await SaveImageAsync(request.AfterImage, absoluteDirectory, relativeDirectory, "after")
            : null;

        var photo = new ClientTreatmentPhoto
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ClientId = request.ClientId,
            BeforeImageUrl = beforeUrl,
            AfterImageUrl = afterUrl,
            CreatedAt = DateTime.UtcNow
        };

        _context.ClientTreatmentPhotos.Add(photo);
        await _context.SaveChangesAsync();

        return Ok(new ClientTreatmentPhotoResponse(
            photo.Id,
            photo.ClientId,
            photo.BeforeImageUrl,
            photo.AfterImageUrl,
            photo.CreatedAt
        ));
    }

    [HttpGet("{clientId}")]
    public async Task<IActionResult> GetByClient(Guid clientId)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var features = await _featureService.GetAsync();
        if (!features.BeforeAfterPhotosEnabled)
            return Ok(new List<ClientTreatmentPhotoResponse>());

        var photos = await _context.ClientTreatmentPhotos
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ClientTreatmentPhotoResponse(
                p.Id,
                p.ClientId,
                p.BeforeImageUrl,
                p.AfterImageUrl,
                p.CreatedAt
            ))
            .ToListAsync();

        return Ok(photos);
    }

    [HttpGet("client/{clientId}")]
    public async Task<IActionResult> GetByClientId(Guid clientId)
    {
        return await GetByClient(clientId);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAll(Guid id)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var photo = await _context.ClientTreatmentPhotos.FindAsync(id);
        if (photo == null)
            return NotFound();

        if (!string.IsNullOrEmpty(photo.BeforeImageUrl))
            DeleteFile(photo.BeforeImageUrl);

        if (!string.IsNullOrEmpty(photo.AfterImageUrl))
            DeleteFile(photo.AfterImageUrl);

        _context.ClientTreatmentPhotos.Remove(photo);
        await _context.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{id}/before")]
    public async Task<IActionResult> DeleteBefore(Guid id)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var photo = await _context.ClientTreatmentPhotos.FindAsync(id);
        if (photo == null)
            return NotFound();

        if (!string.IsNullOrEmpty(photo.BeforeImageUrl))
        {
            DeleteFile(photo.BeforeImageUrl);
            photo.BeforeImageUrl = null;
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}/after")]
    public async Task<IActionResult> DeleteAfter(Guid id)
    {
        if (_tenant.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        var photo = await _context.ClientTreatmentPhotos.FindAsync(id);
        if (photo == null)
            return NotFound();

        if (!string.IsNullOrEmpty(photo.AfterImageUrl))
        {
            DeleteFile(photo.AfterImageUrl);
            photo.AfterImageUrl = null;
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    private static string? ValidateImage(IFormFile? file, string fieldName)
    {
        if (file == null)
            return null;

        if (file.Length == 0)
            return $"{fieldName} is empty.";

        if (file.Length > MaxImageSizeBytes)
            return $"{fieldName} exceeds 5MB limit.";

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return $"{fieldName} has invalid format. Allowed: jpg, png, webp.";

        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedContentTypes.Contains(contentType))
            return $"{fieldName} has invalid content type. Allowed: jpg, png, webp.";

        return null;
    }

    private async Task<string> SaveImageAsync(IFormFile file, string absoluteDirectory, string relativeDirectory, string label)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}-{label}{extension}";
        var absolutePath = Path.Combine(absoluteDirectory, fileName);

        await using var stream = new FileStream(absolutePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return "/" + Path.Combine(relativeDirectory, fileName).Replace('\\', '/');
    }

    private void DeleteFile(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl))
            return;

        var normalized = relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var absolutePath = Path.Combine(webRoot, normalized);

        if (System.IO.File.Exists(absolutePath))
            System.IO.File.Delete(absolutePath);
    }
}
