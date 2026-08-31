using Amazon.S3;
using Amazon.S3.Model;
using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging/instances")]
public class ImagingInstanceStorageController : ControllerBase
{
    private static readonly TimeSpan UploadUrlExpiration = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImagingInstanceStorageController> _logger;

    public ImagingInstanceStorageController(
        AppDbContext db,
        ITenantContext tenantContext,
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<ImagingInstanceStorageController> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _s3Client = s3Client;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("{imagingInstanceId:guid}/storage")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.ImagingGatewayScheme, Policy = PlatformAuthConstants.PolicyImagingGateway)]
    public async Task<IActionResult> UpdateStorage(
        Guid imagingInstanceId,
        [FromBody] UpdateImagingInstanceStorageRequest request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Unauthorized("Tenant not resolved.");
        }

        if (request == null)
        {
            return BadRequest(new { error = "invalid_request", message = "Request body is required." });
        }

        var normalizedStatus = (request.StorageStatus ?? string.Empty).Trim();
        if (!string.Equals(normalizedStatus, ImagingStudyStorageStatuses.LocalAndS3, StringComparison.Ordinal) &&
            !string.Equals(normalizedStatus, ImagingStudyStorageStatuses.UploadFailed, StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                error = "invalid_storage_status",
                message = "StorageStatus must be LocalAndS3 or UploadFailed."
            });
        }

        var tenantId = _tenantContext.TenantId;

        var instance = await _db.ImagingInstances
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == imagingInstanceId, cancellationToken);

        if (instance == null)
        {
            return NotFound(new { error = "imaging_instance_not_found", message = "ImagingInstance not found for tenant." });
        }

        if (string.Equals(normalizedStatus, ImagingStudyStorageStatuses.LocalAndS3, StringComparison.Ordinal))
        {
            var normalizedBucket = request.S3Bucket?.Trim();
            var normalizedKey = request.S3Key?.Trim();

            if (string.IsNullOrWhiteSpace(normalizedBucket) || string.IsNullOrWhiteSpace(normalizedKey))
            {
                return BadRequest(new
                {
                    error = "s3_metadata_required",
                    message = "S3Bucket and S3Key are required when StorageStatus is LocalAndS3."
                });
            }

            instance.S3Bucket = normalizedBucket;
            instance.S3Key = normalizedKey;
            instance.S3ETag = string.IsNullOrWhiteSpace(request.S3ETag) ? null : request.S3ETag.Trim();
            instance.S3UploadedAt = request.S3UploadedAt ?? DateTime.UtcNow;
            instance.StorageStatus = ImagingStudyStorageStatuses.LocalAndS3;
            if (request.FileSizeBytes.HasValue && request.FileSizeBytes.Value > 0)
            {
                instance.FileSizeBytes = request.FileSizeBytes.Value;
            }
        }
        else
        {
            instance.StorageStatus = ImagingStudyStorageStatuses.UploadFailed;
            if (request.FileSizeBytes.HasValue && request.FileSizeBytes.Value > 0)
            {
                instance.FileSizeBytes = request.FileSizeBytes.Value;
            }
        }

        var study = await _db.ImagingStudies
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == instance.ImagingStudyId, cancellationToken);

        if (study != null)
        {
            var statuses = await _db.ImagingInstances
                .Where(i => i.TenantId == tenantId && i.ImagingStudyId == study.Id)
                .Select(i => i.StorageStatus)
                .ToListAsync(cancellationToken);

            if (statuses.Any(s => string.Equals(s, ImagingStudyStorageStatuses.UploadFailed, StringComparison.Ordinal)))
            {
                study.StorageStatus = ImagingStudyStorageStatuses.UploadFailed;
            }
            else if (statuses.Count > 0 && statuses.All(s => string.Equals(s, ImagingStudyStorageStatuses.LocalAndS3, StringComparison.Ordinal)))
            {
                study.StorageStatus = ImagingStudyStorageStatuses.LocalAndS3;
            }
            else
            {
                study.StorageStatus = ImagingStudyStorageStatuses.Local;
            }

            study.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "S3_METADATA_UPDATED tenantId={TenantId} imagingStudyId={ImagingStudyId} imagingSeriesId={ImagingSeriesId} imagingInstanceId={ImagingInstanceId} storageStatus={StorageStatus} s3Key={S3Key} fileSizeBytes={FileSizeBytes}",
            tenantId,
            instance.ImagingStudyId,
            instance.ImagingSeriesId,
            instance.Id,
            instance.StorageStatus,
            instance.S3Key,
            instance.FileSizeBytes);

        var response = new UpdateImagingInstanceStorageResponse(
            instance.Id,
            instance.ImagingStudyId,
            instance.StorageStatus,
            instance.S3Bucket,
            instance.S3Key,
            instance.S3ETag,
            instance.S3UploadedAt,
            instance.FileSizeBytes,
            study?.StorageStatus ?? ImagingStudyStorageStatuses.Local
        );

        return Ok(response);
    }

    [HttpPost("{imagingInstanceId:guid}/upload-url")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.ImagingGatewayScheme, Policy = PlatformAuthConstants.PolicyImagingGateway)]
    public async Task<IActionResult> CreateUploadUrl(
        Guid imagingInstanceId,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Unauthorized("Tenant not resolved.");
        }

        var tenantId = _tenantContext.TenantId;

        // Same-tenant filter on Id+TenantId ensures instances belonging to other tenants are indistinguishable from not found.
        var instance = await _db.ImagingInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == imagingInstanceId, cancellationToken);

        if (instance == null)
        {
            return NotFound(new { error = "imaging_instance_not_found", message = "ImagingInstance not found for tenant." });
        }

        var series = await _db.ImagingSeries
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == instance.ImagingSeriesId, cancellationToken);

        var study = await _db.ImagingStudies
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == instance.ImagingStudyId, cancellationToken);

        if (series == null || study == null)
        {
            return NotFound(new { error = "imaging_instance_not_found", message = "ImagingInstance not found for tenant." });
        }

        var bucketName = _configuration["AWS:ImagingBucketName"];
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            _logger.LogError("IMAGING_UPLOAD_URL_CONFIG_MISSING tenantId={TenantId} imagingInstanceId={ImagingInstanceId}: AWS:ImagingBucketName is not configured.", tenantId, imagingInstanceId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "imaging_bucket_not_configured", message = "Imaging storage is not configured." });
        }

        var objectKey = string.Join('/', "imaging", tenantId.ToString(), study.StudyInstanceUID, series.SeriesInstanceUID, $"{instance.SOPInstanceUID}.dcm");

        var expiresAtUtc = DateTime.UtcNow.Add(UploadUrlExpiration);

        string uploadUrl;
        try
        {
            var presignRequest = new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                Verb = HttpVerb.PUT,
                Expires = expiresAtUtc,
                ContentType = "application/dicom"
            };

            uploadUrl = _s3Client.GetPreSignedURL(presignRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IMAGING_UPLOAD_URL_PRESIGN_FAILED tenantId={TenantId} imagingInstanceId={ImagingInstanceId} objectKey={ObjectKey}", tenantId, imagingInstanceId, objectKey);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "upload_url_generation_failed", message = "Unable to generate an upload URL for storage." });
        }

        _logger.LogInformation(
            "IMAGING_UPLOAD_URL_ISSUED tenantId={TenantId} imagingInstanceId={ImagingInstanceId} objectKey={ObjectKey} expiresAtUtc={ExpiresAtUtc}",
            tenantId,
            imagingInstanceId,
            objectKey,
            expiresAtUtc);

        var response = new ImagingInstanceUploadUrlResponse(
            uploadUrl,
            bucketName,
            objectKey,
            expiresAtUtc
        );

        return Ok(response);
    }
}
