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
        await NormalizeLegacyAdvertisementMediaAsync(settings, cancellationToken);
        return Ok(await BuildSettingsDtoAsync(settings, cancellationToken));
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
        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(await BuildSettingsDtoAsync(settings, cancellationToken));
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

        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedMimeTypes.Contains(file.ContentType?.ToLowerInvariant()))
            return BadRequest("Only image files are allowed (JPG, JPEG, PNG, WebP)");

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

        return Ok(await BuildSettingsDtoAsync(settings, cancellationToken));
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

        return Ok(await BuildSettingsDtoAsync(settings, cancellationToken));
    }

    [HttpPost("settings/advertisement-images")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UploadAdvertisementImages([FromForm] List<IFormFile> files, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        if (files == null || files.Count == 0)
            return BadRequest("No files uploaded");

        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        foreach (var file in files)
        {
            if (file == null || file.Length == 0)
                return BadRequest("One or more uploaded files are empty");

            if (!allowedMimeTypes.Contains(file.ContentType?.ToLowerInvariant()))
                return BadRequest("Only image files are allowed (JPG, JPEG, PNG, WebP)");

            if (file.Length > 8 * 1024 * 1024)
                return BadRequest("File size must not exceed 8MB");
        }

        var settings = await _queueDisplayService.GetOrCreateSettingsAsync(_tenantContext.TenantId, cancellationToken);
        await NormalizeLegacyAdvertisementMediaAsync(settings, cancellationToken);

        var nextOrder = await _db.QueueDisplayAdvertisementImages
            .Where(ad => ad.TenantId == _tenantContext.TenantId && ad.QueueDisplaySettingsId == settings.Id)
            .Select(ad => (int?)ad.DisplayOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var uploadedKeys = new List<string>();

        try
        {
            foreach (var file in files)
            {
                using var stream = file.OpenReadStream();
                var extension = Path.GetExtension(file.FileName);
                var key = $"tenants/{_tenantContext.TenantId}/queue-display/advertisement/images/{Guid.NewGuid()}{extension}";
                var imageUrl = await _fileStorage.UploadAsync(stream, key, file.ContentType);
                uploadedKeys.Add(key);

                nextOrder += 1;
                _db.QueueDisplayAdvertisementImages.Add(new QueueDisplayAdvertisementImage
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.TenantId,
                    QueueDisplaySettingsId = settings.Id,
                    ImageUrl = imageUrl,
                    DisplayOrder = nextOrder,
                    CreatedAt = DateTime.UtcNow,
                });
            }
        }
        catch
        {
            foreach (var key in uploadedKeys)
            {
                await _fileStorage.DeleteAsync(key);
            }

            throw;
        }

        settings.AdvertisementType = QueueDisplayAdvertisementType.Image;
        settings.AdvertisementVideoUrl = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(await BuildSettingsDtoAsync(settings, cancellationToken));
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
        var images = await _db.QueueDisplayAdvertisementImages
            .Where(ad => ad.TenantId == _tenantContext.TenantId && ad.QueueDisplaySettingsId == settings.Id)
            .ToListAsync(cancellationToken);

        foreach (var image in images)
        {
            await DeleteQueueDisplayAdvertisementStorageObjectAsync(image.ImageUrl, _tenantContext.TenantId);
        }

        _db.QueueDisplayAdvertisementImages.RemoveRange(images);

        await DeleteQueueDisplayAdvertisementStorageObjectAsync(settings.AdvertisementImageUrl, _tenantContext.TenantId);
        await DeleteQueueDisplayAdvertisementStorageObjectAsync(settings.AdvertisementVideoUrl, _tenantContext.TenantId);

        settings.AdvertisementType = QueueDisplayAdvertisementType.Image;
        settings.AdvertisementImageUrl = null;
        settings.AdvertisementVideoUrl = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(await BuildSettingsDtoAsync(settings, cancellationToken));
    }

    [HttpDelete("settings/advertisement-images/{imageId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteAdvertisementGalleryImage(Guid imageId, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved");

        if (!await IsQueueDisplayEnabledAsync(cancellationToken))
            return Forbid();

        var image = await _db.QueueDisplayAdvertisementImages
            .FirstOrDefaultAsync(ad => ad.Id == imageId && ad.TenantId == _tenantContext.TenantId, cancellationToken);

        if (image == null)
            return NotFound();

        await DeleteQueueDisplayAdvertisementStorageObjectAsync(image.ImageUrl, _tenantContext.TenantId);
        _db.QueueDisplayAdvertisementImages.Remove(image);

        var remaining = await _db.QueueDisplayAdvertisementImages
            .Where(ad => ad.TenantId == _tenantContext.TenantId && ad.QueueDisplaySettingsId == image.QueueDisplaySettingsId)
            .OrderBy(ad => ad.DisplayOrder)
            .ThenBy(ad => ad.CreatedAt)
            .ToListAsync(cancellationToken);

        for (var i = 0; i < remaining.Count; i++)
        {
            remaining[i].DisplayOrder = i + 1;
        }

        var settings = await _queueDisplayService.GetOrCreateSettingsAsync(_tenantContext.TenantId, cancellationToken);
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(await BuildSettingsDtoAsync(settings, cancellationToken));
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
        return Ok(await BuildSettingsDtoAsync(settings, cancellationToken));
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

    private async Task<QueueDisplaySettingsDto> BuildSettingsDtoAsync(QueueDisplaySettings settings, CancellationToken cancellationToken)
    {
        var advertisementImages = await BuildAdvertisementImageDtosAsync(settings.Id, cancellationToken);

        return new QueueDisplaySettingsDto(
            settings.PublicToken,
            settings.PrivacyMode,
            settings.Theme,
            settings.LogoOverrideUrl,
            advertisementImages
        );
    }

    private async Task<IReadOnlyList<QueueDisplayAdvertisementImageDto>> BuildAdvertisementImageDtosAsync(Guid settingsId, CancellationToken cancellationToken)
    {
        return await _db.QueueDisplayAdvertisementImages
            .AsNoTracking()
            .Where(ad => ad.TenantId == _tenantContext.TenantId && ad.QueueDisplaySettingsId == settingsId)
            .OrderBy(ad => ad.DisplayOrder)
            .ThenBy(ad => ad.CreatedAt)
            .Select(ad => new QueueDisplayAdvertisementImageDto(ad.Id, ad.ImageUrl, ad.DisplayOrder))
            .ToListAsync(cancellationToken);
    }

    private async Task NormalizeLegacyAdvertisementMediaAsync(QueueDisplaySettings settings, CancellationToken cancellationToken)
    {
        var shouldSave = false;

        if (!string.IsNullOrWhiteSpace(settings.AdvertisementVideoUrl))
        {
            await DeleteQueueDisplayAdvertisementStorageObjectAsync(settings.AdvertisementVideoUrl, settings.TenantId);
            settings.AdvertisementVideoUrl = null;
            shouldSave = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.AdvertisementImageUrl))
        {
            var legacyImageUrl = settings.AdvertisementImageUrl.Trim();

            var alreadyExists = await _db.QueueDisplayAdvertisementImages
                .AnyAsync(ad => ad.TenantId == settings.TenantId
                    && ad.QueueDisplaySettingsId == settings.Id
                    && ad.ImageUrl == legacyImageUrl,
                    cancellationToken);

            if (!alreadyExists)
            {
                var nextOrder = (await _db.QueueDisplayAdvertisementImages
                    .Where(ad => ad.TenantId == settings.TenantId && ad.QueueDisplaySettingsId == settings.Id)
                    .Select(ad => (int?)ad.DisplayOrder)
                    .MaxAsync(cancellationToken) ?? 0) + 1;

                _db.QueueDisplayAdvertisementImages.Add(new QueueDisplayAdvertisementImage
                {
                    Id = Guid.NewGuid(),
                    TenantId = settings.TenantId,
                    QueueDisplaySettingsId = settings.Id,
                    ImageUrl = legacyImageUrl,
                    DisplayOrder = nextOrder,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            settings.AdvertisementImageUrl = null;
            shouldSave = true;
        }

        if (settings.AdvertisementType != QueueDisplayAdvertisementType.Image)
        {
            settings.AdvertisementType = QueueDisplayAdvertisementType.Image;
            shouldSave = true;
        }

        if (!shouldSave)
            return;

        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
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

    private async Task DeleteQueueDisplayAdvertisementStorageObjectAsync(string? url, Guid tenantId)
    {
        if (!TryGetQueueDisplayAdvertisementStorageKey(url, tenantId, out var key))
            return;

        await _fileStorage.DeleteAsync(key);
    }

    private static bool TryGetQueueDisplayAdvertisementStorageKey(string? url, Guid tenantId, out string key)
    {
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var parsedKey = uri.AbsolutePath.TrimStart('/');
        var expectedPrefix = $"tenants/{tenantId}/queue-display/advertisement";
        if (!parsedKey.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (parsedKey.Length > expectedPrefix.Length)
        {
            var nextCharacter = parsedKey[expectedPrefix.Length];
            if (nextCharacter != '/' && nextCharacter != '.')
                return false;
        }

        if (!parsedKey.Contains($"tenants/{tenantId}/", StringComparison.OrdinalIgnoreCase))
            return false;

        key = parsedKey;
        return true;
    }

    private async Task<bool> IsQueueDisplayEnabledAsync(CancellationToken cancellationToken)
    {
        var features = await _db.TenantFeatures
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.TenantId == _tenantContext.TenantId, cancellationToken);

        return features?.QueueDisplayEnabled == true;
    }
}
