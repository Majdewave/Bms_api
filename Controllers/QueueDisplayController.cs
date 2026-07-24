using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/queue-display")]
public class QueueDisplayController : ControllerBase
{
    private readonly IQueueDisplayService _queueDisplayService;
    private readonly ITenantContext _tenantContext;
    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;

    public QueueDisplayController(
        IQueueDisplayService queueDisplayService,
        ITenantContext tenantContext,
        AppDbContext db,
        IFileStorage fileStorage)
    {
        _queueDisplayService = queueDisplayService;
        _tenantContext = tenantContext;
        _db = db;
        _fileStorage = fileStorage;
    }

    [HttpGet("public/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublic(string token, [FromQuery] Guid? departmentId, CancellationToken cancellationToken)
    {
        var dto = await _queueDisplayService.GetPublicDisplayAsync(token, departmentId, cancellationToken);
        if (dto == null)
            return NotFound();

        return Ok(dto);
    }

    [HttpGet("settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        var settings = await _queueDisplayService.GetOrCreateSettingsAsync(_tenantContext.TenantId, cancellationToken);
        return Ok(new QueueDisplaySettingsDto(
            settings.PublicToken,
            settings.PrivacyMode,
            settings.Theme,
            settings.LogoOverrideUrl,
            settings.AdvertisementImageUrl
        ));
    }

    [HttpGet("access-link")]
    [Authorize]
    public async Task<IActionResult> GetAccessLink(CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        var settings = await _queueDisplayService.GetOrCreateSettingsAsync(_tenantContext.TenantId, cancellationToken);
        return Ok(new QueueDisplayAccessLinkDto(settings.PublicToken));
    }

    [HttpPut("settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateQueueDisplaySettingsRequest request, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        var settings = await _queueDisplayService.GetOrCreateSettingsAsync(_tenantContext.TenantId, cancellationToken);
        settings.PrivacyMode = request.PrivacyMode;
        settings.Theme = request.Theme;
        settings.LogoOverrideUrl = NormalizeNullable(request.LogoOverrideUrl);
        settings.AdvertisementImageUrl = NormalizeNullable(request.AdvertisementImageUrl);
        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new QueueDisplaySettingsDto(
            settings.PublicToken,
            settings.PrivacyMode,
            settings.Theme,
            settings.LogoOverrideUrl,
            settings.AdvertisementImageUrl
        ));
    }

    [HttpPost("settings/logo-override")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UploadLogoOverride(IFormFile file, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedMimeTypes.Contains(file.ContentType?.ToLowerInvariant()))
            return BadRequest("Only image files are allowed (JPEG, PNG, GIF, WebP)");

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("File size must not exceed 5MB");

        var settings = await _queueDisplayService.GetOrCreateSettingsAsync(_tenantContext.TenantId, cancellationToken);
        await DeleteExistingStorageObjectAsync(settings.LogoOverrideUrl);

        using var stream = file.OpenReadStream();
        var extension = Path.GetExtension(file.FileName);
        var key = $"tenants/{_tenantContext.TenantId}/queue-display/logo-override{extension}";
        settings.LogoOverrideUrl = await _fileStorage.UploadAsync(stream, key, file.ContentType);
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(MapSettingsDto(settings));
    }

    [HttpDelete("settings/logo-override")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteLogoOverride(CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        var settings = await _queueDisplayService.GetOrCreateSettingsAsync(_tenantContext.TenantId, cancellationToken);
        await DeleteExistingStorageObjectAsync(settings.LogoOverrideUrl);
        settings.LogoOverrideUrl = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(MapSettingsDto(settings));
    }

    [HttpPost("settings/advertisement-image")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UploadAdvertisementImage(IFormFile file, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedMimeTypes.Contains(file.ContentType?.ToLowerInvariant()))
            return BadRequest("Only image files are allowed (JPEG, PNG, GIF, WebP)");

        if (file.Length > 8 * 1024 * 1024)
            return BadRequest("File size must not exceed 8MB");

        var settings = await _queueDisplayService.GetOrCreateSettingsAsync(_tenantContext.TenantId, cancellationToken);
        await DeleteExistingStorageObjectAsync(settings.AdvertisementImageUrl);

        using var stream = file.OpenReadStream();
        var extension = Path.GetExtension(file.FileName);
        var key = $"tenants/{_tenantContext.TenantId}/queue-display/advertisement{extension}";
        settings.AdvertisementImageUrl = await _fileStorage.UploadAsync(stream, key, file.ContentType);
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(MapSettingsDto(settings));
    }

    [HttpDelete("settings/advertisement-image")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteAdvertisementImage(CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        var settings = await _queueDisplayService.GetOrCreateSettingsAsync(_tenantContext.TenantId, cancellationToken);
        await DeleteExistingStorageObjectAsync(settings.AdvertisementImageUrl);
        settings.AdvertisementImageUrl = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(MapSettingsDto(settings));
    }

    [HttpPost("settings/regenerate-token")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RegenerateToken(CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        var settings = await _queueDisplayService.RegenerateTokenAsync(_tenantContext.TenantId, cancellationToken);
        return Ok(MapSettingsDto(settings));
    }

    [HttpGet("preview")]
    [Authorize]
    public async Task<IActionResult> Preview([FromQuery] Guid? departmentId, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        var dto = await _queueDisplayService.GetDisplayForTenantAsync(_tenantContext.TenantId, departmentId, cancellationToken);
        if (dto == null)
            return NotFound();

        return Ok(dto);
    }

    private static string? NormalizeNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static QueueDisplaySettingsDto MapSettingsDto(QueueDisplaySettings settings)
    {
        return new QueueDisplaySettingsDto(
            settings.PublicToken,
            settings.PrivacyMode,
            settings.Theme,
            settings.LogoOverrideUrl,
            settings.AdvertisementImageUrl
        );
    }

    private async Task DeleteExistingStorageObjectAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        var key = uri.AbsolutePath.TrimStart('/');
        await _fileStorage.DeleteAsync(key);
    }

    private async Task<bool> IsQueueDisplayEnabledAsync(CancellationToken cancellationToken)
    {
        var features = await _db.TenantFeatures
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.TenantId == _tenantContext.TenantId, cancellationToken);

        return features?.QueueDisplayEnabled == true;
    }
}
