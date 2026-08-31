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
using Npgsql;
using System.Text.Json;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging/studies")]
public class ImagingStudiesController : ControllerBase
{
    private const string StudyUidUniqueConstraintName = "IX_ImagingStudies_TenantId_StudyInstanceUID";
    private const string SeriesUidUniqueConstraintName = "IX_ImagingSeries_TenantId_SeriesInstanceUID";
    private const string SopUidUniqueConstraintName = "IX_ImagingInstances_TenantId_SOPInstanceUID";

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImagingStudiesController> _logger;

    public ImagingStudiesController(AppDbContext db, ITenantContext tenantContext, IAmazonS3 s3Client, IConfiguration configuration, ILogger<ImagingStudiesController> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _s3Client = s3Client;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("/api/clients/{clientId:guid}/imaging/studies")]
    [Authorize(Policy = "view_clients")]
    public async Task<IActionResult> GetClientImagingStudies(Guid clientId, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Unauthorized("Tenant not resolved.");
        }

        var clientExists = await _db.Clients
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == clientId, cancellationToken);

        if (!clientExists)
        {
            return NotFound(new { error = "client_not_found", message = "Client not found for the current tenant." });
        }

        var studies = await _db.ImagingStudies
            .AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId && s.ClientId == clientId)
            .OrderByDescending(s => s.ReceivedAt)
            .Select(s => new ClientImagingStudyDto(
                s.Id,
                s.ImagingOrderId,
                s.AccessionNumber,
                s.StudyInstanceUID,
                s.Modality,
                s.Status,
                s.ReceivedAt,
                s.StorageStatus,
                s.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(studies);
    }

    [HttpGet("/api/imaging/studies/{studyId:guid}")]
    [Authorize(Policy = "view_clients")]
    public async Task<IActionResult> GetStudyHierarchy(Guid studyId, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Unauthorized("Tenant not resolved.");
        }

        var study = await _db.ImagingStudies
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId && s.Id == studyId, cancellationToken);

        if (study == null)
        {
            return NotFound(new { error = "imaging_study_not_found", message = "Imaging study not found for the current tenant." });
        }

        var series = await _db.ImagingSeries
            .AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId && s.ImagingStudyId == studyId)
            .OrderBy(s => s.SeriesNumber ?? int.MaxValue)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        var instances = await _db.ImagingInstances
            .AsNoTracking()
            .Where(i => i.TenantId == _tenantContext.TenantId && i.ImagingStudyId == studyId)
            .OrderBy(i => i.InstanceNumber ?? int.MaxValue)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var response = new
        {
            Id = study.Id,
            ImagingOrderId = study.ImagingOrderId,
            AccessionNumber = study.AccessionNumber,
            StudyInstanceUID = study.StudyInstanceUID,
            Modality = study.Modality,
            Status = study.Status,
            ReceivedAt = study.ReceivedAt,
            StorageStatus = study.StorageStatus,
            CreatedAt = study.CreatedAt,
            Series = series.Select(s => new
            {
                Id = s.Id,
                SeriesInstanceUID = s.SeriesInstanceUID,
                Modality = s.Modality,
                SeriesNumber = s.SeriesNumber,
                SeriesDescription = s.SeriesDescription,
                CreatedAt = s.CreatedAt,
                Instances = instances
                    .Where(i => i.ImagingSeriesId == s.Id)
                    .Select(i => new
                    {
                        Id = i.Id,
                        SOPInstanceUID = i.SOPInstanceUID,
                        SOPClassUID = i.SOPClassUID,
                        InstanceNumber = i.InstanceNumber,
                        FileSizeBytes = i.FileSizeBytes,
                        StorageStatus = i.StorageStatus,
                        ReceivedAt = i.ReceivedAt
                    })
                    .ToList()
            }).ToList()
        };

        return Ok(response);
    }

    [HttpGet("/api/imaging/instances/{instanceId:guid}/file")]
    [Authorize(Policy = "view_clients")]
    public async Task<IActionResult> GetInstanceFile(Guid instanceId, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Unauthorized("Tenant not resolved.");
        }

        var instance = await _db.ImagingInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == _tenantContext.TenantId && i.Id == instanceId, cancellationToken);

        if (instance == null)
        {
            return NotFound(new { error = "imaging_instance_not_found", message = "Imaging instance not found for the current tenant." });
        }

        if (!string.Equals(instance.StorageStatus, ImagingStudyStorageStatuses.LocalAndS3, StringComparison.Ordinal))
        {
            return Conflict(new { error = "imaging_instance_not_available", message = "Imaging instance is not yet available for secure retrieval." });
        }

        if (string.IsNullOrWhiteSpace(instance.S3Bucket) || string.IsNullOrWhiteSpace(instance.S3Key))
        {
            return Conflict(new { error = "imaging_instance_s3_metadata_missing", message = "Imaging instance does not have S3 metadata yet." });
        }

        var bucket = instance.S3Bucket.Trim();
        var key = instance.S3Key.Trim();
        var request = new GetObjectRequest
        {
            BucketName = bucket,
            Key = key
        };

        try
        {
            var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            var contentType = response.Headers.ContentType ?? "application/octet-stream";
            var fileName = Path.GetFileName(key) ?? $"study-{instanceId}.dcm";

            return File(response.ResponseStream, contentType, fileName);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 read failed for imaging instance {ImagingInstanceId} in bucket {Bucket} key {Key}.", instanceId, bucket, key);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "s3_read_failed", message = "Unable to retrieve the imaging file from storage." });
        }
    }

    [HttpPost("register")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.ImagingGatewayScheme, Policy = PlatformAuthConstants.PolicyImagingGateway)]
    public async Task<IActionResult> Register([FromBody] RegisterImagingStudyRequest request, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved.");

        if (request == null)
            return BadRequest(new { error = "invalid_request", message = "Request body is required." });

        var normalizedAccession = request.AccessionNumber?.Trim().ToUpperInvariant();
        var normalizedStudyUid = request.StudyInstanceUID?.Trim();
        var normalizedModality = request.Modality?.Trim().ToUpperInvariant();
        var normalizedLocalStoragePath = request.LocalStoragePath?.Trim();
        var normalizedSeriesUid = request.SeriesInstanceUID?.Trim();
        var normalizedSopInstanceUid = request.SOPInstanceUID?.Trim();
        var normalizedSopClassUid = request.SOPClassUID?.Trim();
        var normalizedSeriesDescription = string.IsNullOrWhiteSpace(request.SeriesDescription) ? null : request.SeriesDescription.Trim();
        var normalizedLocalFilePath = request.LocalFilePath?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedAccession))
            return BadRequest(new { error = "accession_required", message = "AccessionNumber is required." });

        if (normalizedAccession.Length > 32)
            return BadRequest(new { error = "accession_too_long", message = "AccessionNumber must be at most 32 characters." });

        if (string.IsNullOrWhiteSpace(normalizedStudyUid))
            return BadRequest(new { error = "study_uid_required", message = "StudyInstanceUID is required." });

        if (normalizedStudyUid.Length > 64)
            return BadRequest(new { error = "study_uid_too_long", message = "StudyInstanceUID must be at most 64 characters." });

        if (string.IsNullOrWhiteSpace(normalizedModality))
            return BadRequest(new { error = "modality_required", message = "Modality is required." });

        if (normalizedModality.Length > 16)
            return BadRequest(new { error = "modality_too_long", message = "Modality must be at most 16 characters." });

        if (string.IsNullOrWhiteSpace(normalizedLocalStoragePath))
            return BadRequest(new { error = "local_storage_path_required", message = "LocalStoragePath is required." });

        if (normalizedLocalStoragePath.Length > 1024)
            return BadRequest(new { error = "local_storage_path_too_long", message = "LocalStoragePath must be at most 1024 characters." });

        if (string.IsNullOrWhiteSpace(normalizedSeriesUid))
            return BadRequest(new { error = "series_uid_required", message = "SeriesInstanceUID is required." });

        if (normalizedSeriesUid.Length > 64)
            return BadRequest(new { error = "series_uid_too_long", message = "SeriesInstanceUID must be at most 64 characters." });

        if (string.IsNullOrWhiteSpace(normalizedSopInstanceUid))
            return BadRequest(new { error = "sop_instance_uid_required", message = "SOPInstanceUID is required." });

        if (normalizedSopInstanceUid.Length > 64)
            return BadRequest(new { error = "sop_instance_uid_too_long", message = "SOPInstanceUID must be at most 64 characters." });

        if (string.IsNullOrWhiteSpace(normalizedSopClassUid))
            return BadRequest(new { error = "sop_class_uid_required", message = "SOPClassUID is required." });

        if (normalizedSopClassUid.Length > 64)
            return BadRequest(new { error = "sop_class_uid_too_long", message = "SOPClassUID must be at most 64 characters." });

        if (!string.IsNullOrEmpty(normalizedSeriesDescription) && normalizedSeriesDescription.Length > 256)
            return BadRequest(new { error = "series_description_too_long", message = "SeriesDescription must be at most 256 characters." });

        if (string.IsNullOrWhiteSpace(normalizedLocalFilePath))
            return BadRequest(new { error = "local_file_path_required", message = "LocalFilePath is required." });

        if (normalizedLocalFilePath.Length > 1024)
            return BadRequest(new { error = "local_file_path_too_long", message = "LocalFilePath must be at most 1024 characters." });

        var tenantId = _tenantContext.TenantId;
        var executionStrategy = _db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync<IActionResult>(async () =>
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

                var imagingOrder = await _db.ImagingOrders
                    .AsNoTracking()
                    .Where(io => io.TenantId == tenantId)
                    .Where(io => io.AccessionNumber == normalizedAccession)
                    .Select(io => new { io.Id, io.ClientId })
                    .FirstOrDefaultAsync(cancellationToken);

                if (imagingOrder == null)
                {
                    return NotFound(new { error = "imaging_order_not_found", message = "No imaging order found for accession number." });
                }

                var studyCreated = false;
                var seriesCreated = false;
                var instanceCreated = false;

                var study = await _db.ImagingStudies
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.StudyInstanceUID == normalizedStudyUid, cancellationToken);

                if (study == null)
                {
                    study = new ImagingStudy
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        ClientId = imagingOrder.ClientId,
                        ImagingOrderId = imagingOrder.Id,
                        AccessionNumber = normalizedAccession,
                        StudyInstanceUID = normalizedStudyUid,
                        Modality = normalizedModality,
                        Status = ImagingStudyStatuses.Received,
                        ReceivedAt = DateTime.UtcNow,
                        LocalStoragePath = normalizedLocalStoragePath,
                        StorageStatus = ImagingStudyStorageStatuses.Local,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = null
                    };

                    _db.ImagingStudies.Add(study);
                    studyCreated = true;
                }
                else if (study.ClientId != imagingOrder.ClientId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogWarning("ImagingStudy client mismatch for TenantId {TenantId}, StudyInstanceUID {StudyInstanceUID}. Existing ClientId {ExistingClientId}, MatchedOrderClientId {MatchedOrderClientId}.", tenantId, normalizedStudyUid, study.ClientId, imagingOrder.ClientId);
                    return Conflict(new { error = "study_client_mismatch", message = "Existing study belongs to a different client." });
                }

                var series = await _db.ImagingSeries
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SeriesInstanceUID == normalizedSeriesUid, cancellationToken);

                if (series == null)
                {
                    series = new ImagingSeries
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        ImagingStudyId = study.Id,
                        SeriesInstanceUID = normalizedSeriesUid,
                        Modality = normalizedModality,
                        SeriesNumber = request.SeriesNumber,
                        SeriesDescription = normalizedSeriesDescription,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.ImagingSeries.Add(series);
                    seriesCreated = true;
                }
                else if (series.ImagingStudyId != study.Id)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogWarning("ImagingSeries study mismatch for TenantId {TenantId}, SeriesInstanceUID {SeriesInstanceUID}. Existing ImagingStudyId {ExistingImagingStudyId}, Resolved ImagingStudyId {ResolvedImagingStudyId}.", tenantId, normalizedSeriesUid, series.ImagingStudyId, study.Id);
                    return Conflict(new { error = "series_study_mismatch", message = "Existing series belongs to a different study." });
                }

                var instance = await _db.ImagingInstances
                    .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.SOPInstanceUID == normalizedSopInstanceUid, cancellationToken);

                if (instance == null)
                {
                    instance = new ImagingInstance
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        ImagingStudyId = study.Id,
                        ImagingSeriesId = series.Id,
                        SOPInstanceUID = normalizedSopInstanceUid,
                        SOPClassUID = normalizedSopClassUid,
                        InstanceNumber = request.InstanceNumber,
                        LocalFilePath = normalizedLocalFilePath,
                        StorageStatus = ImagingStudyStorageStatuses.Local,
                        FileSizeBytes = request.FileSizeBytes,
                        ReceivedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.ImagingInstances.Add(instance);
                    instanceCreated = true;
                }
                else
                {
                    if (instance.ImagingStudyId != study.Id)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        _logger.LogWarning("ImagingInstance study mismatch for TenantId {TenantId}, SOPInstanceUID {SOPInstanceUID}. Existing ImagingStudyId {ExistingImagingStudyId}, Resolved ImagingStudyId {ResolvedImagingStudyId}.", tenantId, normalizedSopInstanceUid, instance.ImagingStudyId, study.Id);
                        return Conflict(new { error = "instance_study_mismatch", message = "Existing instance belongs to a different study." });
                    }

                    if (instance.ImagingSeriesId != series.Id)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        _logger.LogWarning("ImagingInstance series mismatch for TenantId {TenantId}, SOPInstanceUID {SOPInstanceUID}. Existing ImagingSeriesId {ExistingImagingSeriesId}, Resolved ImagingSeriesId {ResolvedImagingSeriesId}.", tenantId, normalizedSopInstanceUid, instance.ImagingSeriesId, series.Id);
                        return Conflict(new { error = "instance_series_mismatch", message = "Existing instance belongs to a different series." });
                    }

                    if (string.Equals(instance.StorageStatus, ImagingStudyStorageStatuses.LocalAndS3, StringComparison.Ordinal))
                    {
                        _logger.LogInformation(
                            "IMAGING_REGISTER_IDEMPOTENT_DUPLICATE TenantId {TenantId} SOPInstanceUID {SOPInstanceUID}",
                            tenantId,
                            normalizedSopInstanceUid);

                        instance.LocalFilePath = normalizedLocalFilePath;
                        instance.FileSizeBytes = request.FileSizeBytes;
                        instance.ReceivedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        instance.LocalFilePath = normalizedLocalFilePath;
                        instance.FileSizeBytes = request.FileSizeBytes;
                        instance.StorageStatus = ImagingStudyStorageStatuses.Local;
                        instance.ReceivedAt = DateTime.UtcNow;
                    }
                }

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return Ok(ToResponse(
                        study,
                        series,
                        instance,
                        studyCreated,
                        seriesCreated,
                        instanceCreated));
                }
                catch (DbUpdateException ex) when (IsExpectedUniqueRace(ex) && attempt == 1)
                {
                    LogPersistenceException(
                        ex,
                        tenantId,
                        normalizedAccession,
                        normalizedStudyUid,
                        normalizedSeriesUid,
                        normalizedSopInstanceUid,
                        attempt,
                        "expected_unique_race_retry");
                    await transaction.RollbackAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    continue;
                }
                catch (DbUpdateException ex)
                {
                    LogPersistenceException(
                        ex,
                        tenantId,
                        normalizedAccession,
                        normalizedStudyUid,
                        normalizedSeriesUid,
                        normalizedSopInstanceUid,
                        attempt,
                        "db_update_failure");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Imaging register failed with non-DB exception. tenantId={TenantId}, accession={AccessionNumber}, studyUid={StudyInstanceUID}, seriesUid={SeriesInstanceUID}, sopUid={SOPInstanceUID}, attempt={Attempt}, exceptionType={ExceptionType}, innerType={InnerType}, message={Message}",
                        tenantId,
                        normalizedAccession,
                        normalizedStudyUid,
                        normalizedSeriesUid,
                        normalizedSopInstanceUid,
                        attempt,
                        ex.GetType().FullName,
                        ex.InnerException?.GetType().FullName,
                        ex.Message);
                    throw;
                }
            }

            // If a concurrent insert happened twice in a row, allow normal error behavior.
            throw new DbUpdateException("Unable to register imaging hierarchy due to concurrent writes.");
        });
    }

    private static RegisterImagingStudyResponse ToResponse(
        ImagingStudy study,
        ImagingSeries series,
        ImagingInstance instance,
        bool studyCreated,
        bool seriesCreated,
        bool instanceCreated)
    {
        return new RegisterImagingStudyResponse(
            study.Id,
            series.Id,
            instance.Id,
            study.TenantId,
            study.ClientId,
            study.ImagingOrderId,
            study.AccessionNumber,
            study.StudyInstanceUID,
            series.SeriesInstanceUID,
            instance.SOPInstanceUID,
            study.Modality,
            studyCreated,
            seriesCreated,
            instanceCreated,
            study.Status,
            instance.StorageStatus,
            studyCreated || seriesCreated || instanceCreated
        );
    }

    private static bool IsExpectedUniqueRace(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg)
        {
            return pg.SqlState == PostgresErrorCodes.UniqueViolation &&
                   (string.Equals(pg.ConstraintName, StudyUidUniqueConstraintName, StringComparison.Ordinal) ||
                    string.Equals(pg.ConstraintName, SeriesUidUniqueConstraintName, StringComparison.Ordinal) ||
                    string.Equals(pg.ConstraintName, SopUidUniqueConstraintName, StringComparison.Ordinal));
        }

        return false;
    }

    private void LogPersistenceException(
        DbUpdateException ex,
        Guid tenantId,
        string accessionNumber,
        string studyInstanceUid,
        string seriesInstanceUid,
        string sopInstanceUid,
        int attempt,
        string stage)
    {
        var postgres = ex.InnerException as PostgresException;
        _logger.LogError(
            ex,
            "Imaging register DB failure. stage={Stage}, tenantId={TenantId}, accession={AccessionNumber}, studyUid={StudyInstanceUID}, seriesUid={SeriesInstanceUID}, sopUid={SOPInstanceUID}, attempt={Attempt}, exceptionType={ExceptionType}, innerType={InnerType}, sqlState={SqlState}, constraint={ConstraintName}, table={TableName}, detail={Detail}, message={Message}, trackedEntities={TrackedEntities}",
            stage,
            tenantId,
            accessionNumber,
            studyInstanceUid,
            seriesInstanceUid,
            sopInstanceUid,
            attempt,
            ex.GetType().FullName,
            ex.InnerException?.GetType().FullName,
            postgres?.SqlState,
            postgres?.ConstraintName,
            postgres?.TableName,
            postgres?.Detail,
            postgres?.MessageText ?? ex.Message,
            DescribeTrackedEntities());
    }

    private string DescribeTrackedEntities()
    {
        var tracked = _db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => new
            {
                Entity = e.Entity.GetType().Name,
                e.State,
                TenantId = TryReadProperty(e, "TenantId"),
                ImagingStudyId = TryReadProperty(e, "ImagingStudyId"),
                ImagingSeriesId = TryReadProperty(e, "ImagingSeriesId"),
                StudyInstanceUID = TryReadProperty(e, "StudyInstanceUID"),
                SeriesInstanceUID = TryReadProperty(e, "SeriesInstanceUID"),
                SOPInstanceUID = TryReadProperty(e, "SOPInstanceUID")
            })
            .ToList();

        return JsonSerializer.Serialize(tracked);
    }

    private static object? TryReadProperty(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string propertyName)
    {
        try
        {
            return entry.Properties.FirstOrDefault(p => p.Metadata.Name == propertyName)?.CurrentValue;
        }
        catch
        {
            return null;
        }
    }
}
