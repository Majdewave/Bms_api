using Amazon.S3;
using Amazon.S3.Model;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Security.Claims;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/interpreter/interpretation-requests")]
[Authorize]
public class InterpreterPortalController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<InterpreterPortalController> _logger;
    private readonly InterpretationPdfService _interpretationPdfService;
    private readonly string _assetBucket;

    public InterpreterPortalController(
        AppDbContext context,
        ITenantContext tenant,
        IAmazonS3 s3Client,
        ILogger<InterpreterPortalController> logger,
        IConfiguration configuration,
        InterpretationPdfService interpretationPdfService)
    {
        _context = context;
        _tenant = tenant;
        _s3Client = s3Client;
        _logger = logger;
        _interpretationPdfService = interpretationPdfService;
        _assetBucket = configuration["AWS:BucketName"] ?? string.Empty;
    }

    [HttpGet("/api/interpreter/profile")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == userId &&
                candidate.TenantId == _tenant.TenantId,
                cancellationToken);

        return user == null ? NotFound() : Ok(ToProfileDto(user));
    }

    [HttpPut("/api/interpreter/profile")]
    public async Task<IActionResult> UpdateProfile(
        UpdateInterpreterProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest("Full name is required.");

        var user = await _context.Users
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == userId &&
                candidate.TenantId == _tenant.TenantId,
                cancellationToken);

        if (user == null)
            return NotFound();

        user.FullName = request.FullName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        user.LicenseNumber = string.IsNullOrWhiteSpace(request.LicenseNumber) ? null : request.LicenseNumber.Trim();
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ToProfileDto(user));
    }

    [HttpPost("/api/interpreter/profile/stamp")]
    public async Task<IActionResult> UploadProfileStamp(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        const long maxFileSize = 2 * 1024 * 1024;
        if (file == null || file.Length == 0)
            return BadRequest("No stamp file was provided.");
        if (file.Length > maxFileSize)
            return BadRequest("Stamp file must not exceed 2 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".png", ".jpg", ".jpeg" };
        if (!allowed.Contains(extension) || file.ContentType is not ("image/png" or "image/jpeg"))
            return BadRequest("Stamp must be a PNG or JPEG image.");

        await using var stream = file.OpenReadStream();
        if (!await HasValidImageSignatureAsync(stream, extension, cancellationToken))
            return BadRequest("Stamp file content does not match its image type.");
        stream.Position = 0;

        var user = await _context.Users
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == userId &&
                candidate.TenantId == _tenant.TenantId,
                cancellationToken);
        if (user == null)
            return NotFound();

        var oldStampUrl = user.StampUrl;
        var oldUseStamp = user.UseStamp;
        var oldKey = TryGetStorageKey(oldStampUrl);
        var key = $"tenants/{_tenant.TenantId}/interpreters/{userId}/stamp-{Guid.NewGuid():N}{extension}";
        await UploadAssetAsync(stream, key, file.ContentType, cancellationToken);

        try
        {
            user.StampUrl = BuildAssetUrl(key);
            user.UseStamp = true;
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            user.StampUrl = oldStampUrl;
            user.UseStamp = oldUseStamp;
            await TryDeleteObjectAsync(key, userId, "new stamp cleanup after database failure");
            _logger.LogWarning(exception, "Interpreter stamp database update failed for tenant {TenantId}, user {UserId}.", _tenant.TenantId, userId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to save the interpreter stamp.");
        }

        if (oldKey != null)
            await TryDeleteObjectAsync(oldKey, userId, "old stamp replacement cleanup");

        return Ok(ToProfileDto(user));
    }

    [HttpGet("/api/interpreter/profile/stamp")]
    public async Task<IActionResult> GetProfileStamp(CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(candidate =>
            candidate.Id == userId && candidate.TenantId == _tenant.TenantId,
            cancellationToken);
        if (user == null || string.IsNullOrWhiteSpace(user.StampUrl) || !Uri.TryCreate(user.StampUrl, UriKind.Absolute, out var uri))
            return NotFound();

        var response = await _s3Client.GetObjectAsync(_assetBucket, uri.AbsolutePath.TrimStart('/'), cancellationToken);
        return File(response.ResponseStream, response.Headers.ContentType ?? "application/octet-stream");
    }

    [HttpDelete("/api/interpreter/profile/stamp")]
    public async Task<IActionResult> DeleteProfileStamp(CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var user = await _context.Users.FirstOrDefaultAsync(candidate =>
            candidate.Id == userId && candidate.TenantId == _tenant.TenantId,
            cancellationToken);
        if (user == null)
            return NotFound();

        var oldKey = TryGetStorageKey(user.StampUrl);
        user.StampUrl = null;
        user.UseStamp = false;
        await _context.SaveChangesAsync(cancellationToken);
        if (oldKey != null)
            await TryDeleteObjectAsync(oldKey, userId, "stamp removal cleanup");
        return NoContent();
    }

    private async Task<string> UploadAssetAsync(Stream stream, string key, string contentType, CancellationToken cancellationToken)
    {
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _assetBucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType
        }, cancellationToken);

        return BuildAssetUrl(key);
    }

    private string BuildAssetUrl(string key) => $"https://{_assetBucket}.s3.amazonaws.com/{key}";

    private static string? TryGetStorageKey(string? stampUrl)
    {
        return Uri.TryCreate(stampUrl, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.TrimStart('/')
            : null;
    }

    private async Task TryDeleteObjectAsync(string key, Guid userId, string operation)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(_assetBucket, key);
        }
        catch (Exception)
        {
            _logger.LogWarning("Interpreter stamp storage cleanup failed during {Operation} for tenant {TenantId}, user {UserId}.", operation, _tenant.TenantId, userId);
        }
    }

    private static async Task<bool> HasValidImageSignatureAsync(Stream stream, string extension, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        var bytesRead = 0;
        while (bytesRead < header.Length)
        {
            var read = await stream.ReadAsync(header.AsMemory(bytesRead), cancellationToken);
            if (read == 0) break;
            bytesRead += read;
        }

        if (extension == ".png")
            return bytesRead == 8 && header.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        return bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
    }

    private static InterpreterProfileDto ToProfileDto(User user) => new(
        user.Id,
        user.Email,
        user.FullName ?? string.Empty,
        user.Phone,
        user.LicenseNumber,
        user.UseStamp && !string.IsNullOrWhiteSpace(user.StampUrl));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var requests = await AssignedRequests(userId)
            .OrderByDescending(r => r.RequestedAt)
            .Select(r => new InterpreterRequestListItemDto(
                r.Id,
                r.ImagingOrderId,
                r.ImagingStudyId,
                r.ImagingOrder.AccessionNumber,
                r.ImagingOrder.Modality,
                r.ImagingOrder.ClientId,
                r.ImagingOrder.Client.FullName,
                r.Status,
                r.RequestedAt,
                r.StartedAt,
                r.CompletedAt))
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    [HttpGet("{requestId:guid}/case")]
    public async Task<IActionResult> GetCase(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        // Important:
        // Project every value required by the portal directly in SQL.
        // Do not materialize InterpretationRequest and later depend on
        // navigation properties being loaded.
        var request = await AssignedRequests(userId)
            .Where(r => r.Id == requestId)
            .Select(r => new
            {
                RequestId = r.Id,
                r.Status,
                r.RequestedAt,
                r.StartedAt,
                r.CompletedAt,

                ClientId = r.ImagingOrder.ClientId,
                ClientDisplayName = r.ImagingOrder.Client.FullName,

                ImagingOrderId = r.ImagingOrderId,
                AccessionNumber = r.ImagingOrder.AccessionNumber,
                Modality = r.ImagingOrder.Modality,
                ScheduledStartTime = r.ImagingOrder.ScheduledStartTime,
                ReferringDoctorName = r.ImagingOrder.ReferringDoctorName,

                ImagingStudyId = r.ImagingStudyId,
                StudyInstanceUID = r.ImagingStudy.StudyInstanceUID,
                StudyStatus = r.ImagingStudy.Status,
                ReceivedAt = r.ImagingStudy.ReceivedAt,

                Referral = r.ImagingOrder.Documents
                    .Where(d =>
                        d.DocumentType == ImagingOrderDocumentTypes.Referral &&
                        !d.IsDeleted)
                    .Select(d => new InterpreterReferralMetadataDto(
                        d.Id,
                        d.OriginalFileName,
                        d.ContentType,
                        d.FileSize))
                    .FirstOrDefault(),

                Report = _context.InterpretationReports
                    .AsNoTracking()
                    .Where(report =>
                        report.TenantId == _tenant.TenantId &&
                        report.InterpretationRequestId == r.Id)
                    .Select(report => new InterpreterReportDto(
                        report.Id,
                        report.Content,
                        report.CreatedAt,
                        report.UpdatedAt))
                    .FirstOrDefault(),

                HasFinalPdf = _context.InterpretationReportDocuments
                    .Any(document =>
                        document.TenantId == _tenant.TenantId &&
                        document.InterpretationRequestId == r.Id &&
                        document.Version == 1 &&
                        !document.IsDeleted)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (request == null)
            return NotFound();

        // Load only series/instances belonging to the exact study
        // assigned in this InterpretationRequest.
        var series = await _context.ImagingSeries
            .AsNoTracking()
            .Where(s =>
                s.TenantId == _tenant.TenantId &&
                s.ImagingStudyId == request.ImagingStudyId)
            .OrderBy(s => s.SeriesNumber)
            .Select(s => new InterpreterSeriesDto(
                s.Id,
                s.SeriesInstanceUID,
                s.Modality,
                s.SeriesNumber,
                s.SeriesDescription,

                _context.ImagingInstances
                    .AsNoTracking()
                    .Where(i =>
                        i.TenantId == _tenant.TenantId &&
                        i.ImagingStudyId == request.ImagingStudyId &&
                        i.ImagingSeriesId == s.Id)
                    .OrderBy(i => i.InstanceNumber)
                    .Select(i => new InterpreterInstanceDto(
                        i.Id,
                        i.SOPInstanceUID,
                        i.SOPClassUID,
                        i.InstanceNumber,
                        i.FileSizeBytes))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return Ok(new InterpreterCaseDto(
            request.RequestId,
            request.Status,
            request.RequestedAt,
            request.StartedAt,
            request.CompletedAt,

            request.ClientId,
            request.ClientDisplayName,

            request.ImagingOrderId,
            request.AccessionNumber,
            request.Modality,
            request.ScheduledStartTime,
            request.ReferringDoctorName,

            request.ImagingStudyId,
            request.StudyInstanceUID,
            request.StudyStatus,
            request.ReceivedAt,

            request.Referral,
            series,
            request.Report,
            request.HasFinalPdf));
    }

    [HttpGet("{requestId:guid}/report/pdf")]
    public async Task<IActionResult> GetReportPdf(
        Guid requestId,
        [FromQuery] bool download,
        CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var document = await AssignedRequests(userId)
            .Where(request => request.Id == requestId)
            .SelectMany(request => _context.InterpretationReportDocuments
                .Where(candidate =>
                    candidate.TenantId == _tenant.TenantId &&
                    candidate.InterpretationRequestId == request.Id &&
                    candidate.Version == 1 &&
                    !candidate.IsDeleted))
            .Select(candidate => new
            {
                candidate.Content,
                candidate.ContentType,
                candidate.FileName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (document == null)
            return NotFound();

        if (!download)
            Response.Headers.ContentDisposition = $"inline; filename=\"{document.FileName}\"";

        return download
            ? File(document.Content, document.ContentType, document.FileName, enableRangeProcessing: true)
            : File(document.Content, document.ContentType, enableRangeProcessing: true);
    }

    [HttpPut("{requestId:guid}/report")]
    public async Task<IActionResult> SaveReport(
        Guid requestId,
        [FromBody] SaveInterpreterReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var interpretationRequest = await _context.InterpretationRequests
            .Include(request => request.ImagingOrder)
                .ThenInclude(order => order.Client)
            .Include(request => request.ImagingOrder)
                .ThenInclude(order => order.Service)
            .FirstOrDefaultAsync(r =>
                r.TenantId == _tenant.TenantId &&
                r.AssignedInterpreterId == userId &&
                r.Id == requestId,
                cancellationToken);

        if (interpretationRequest == null)
            return NotFound();

        var existingDocument = await _context.InterpretationReportDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(document =>
                document.TenantId == _tenant.TenantId &&
                document.InterpretationRequestId == interpretationRequest.Id &&
                document.Version == 1 &&
                !document.IsDeleted,
                cancellationToken);

        if (string.Equals(interpretationRequest.Status, InterpretationRequestStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            if (existingDocument == null)
            {
                _logger.LogError("Completed interpretation request {RequestId} has no active version 1 PDF document.", requestId);
                return Conflict(new { error = "interpretation_document_missing" });
            }

            return Ok(new
            {
                requestId = interpretationRequest.Id,
                status = interpretationRequest.Status,
                startedAt = interpretationRequest.StartedAt,
                completedAt = interpretationRequest.CompletedAt,
                documentId = existingDocument.Id,
                documentVersion = existingDocument.Version
            });
        }


        var now = DateTime.UtcNow;
        var contentValue = request.Content ?? string.Empty;

        var report = await _context.InterpretationReports
            .FirstOrDefaultAsync(r =>
                r.TenantId == _tenant.TenantId &&
                r.InterpretationRequestId == interpretationRequest.Id,
                cancellationToken);

        if (report == null)
        {
            report = new InterpretationReport
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                InterpretationRequestId = interpretationRequest.Id,
                Content = contentValue,
                CreatedAt = now
            };

            _context.InterpretationReports.Add(report);
        }
        else
        {
            report.Content = contentValue;
            report.UpdatedAt = now;
        }

        if (string.Equals(
                interpretationRequest.Status,
                InterpretationRequestStatuses.Pending,
                StringComparison.OrdinalIgnoreCase))
        {
            interpretationRequest.Status = InterpretationRequestStatuses.InProgress;
            interpretationRequest.StartedAt ??= now;
        }

        interpretationRequest.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new InterpreterReportDto(
            report.Id,
            report.Content,
            report.CreatedAt,
            report.UpdatedAt));
    }
    [HttpPost("{requestId:guid}/complete")]
    public async Task<IActionResult> CompleteInterpretation(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var interpretationRequest = await _context.InterpretationRequests
            .Include(request => request.ImagingOrder)
                .ThenInclude(order => order.Client)
            .Include(request => request.ImagingOrder)
                .ThenInclude(order => order.Service)
            .FirstOrDefaultAsync(r =>
                r.TenantId == _tenant.TenantId &&
                r.AssignedInterpreterId == userId &&
                r.Id == requestId,
                cancellationToken);

        if (interpretationRequest == null)
            return NotFound();

        var existingDocument = await _context.InterpretationReportDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(document =>
                document.TenantId == _tenant.TenantId &&
                document.InterpretationRequestId == interpretationRequest.Id &&
                document.Version == 1 &&
                !document.IsDeleted,
                cancellationToken);

        if (string.Equals(interpretationRequest.Status, InterpretationRequestStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            if (existingDocument == null)
            {
                _logger.LogError("Completed interpretation request {RequestId} has no active version 1 PDF document.", requestId);
                return Conflict(new { error = "interpretation_document_missing" });
            }

            return Ok(new
            {
                requestId = interpretationRequest.Id,
                status = interpretationRequest.Status,
                startedAt = interpretationRequest.StartedAt,
                completedAt = interpretationRequest.CompletedAt,
                documentId = existingDocument.Id,
                documentVersion = existingDocument.Version
            });
        }

        var report = await _context.InterpretationReports
            .FirstOrDefaultAsync(r =>
                r.TenantId == _tenant.TenantId &&
                r.InterpretationRequestId == interpretationRequest.Id,
                cancellationToken);

        if (report == null || string.IsNullOrWhiteSpace(report.Content))
        {
            return BadRequest(new
            {
                error = "interpretation_content_required",
                message = "Interpretation content is required before completion."
            });
        }

        var now = DateTime.UtcNow;
        interpretationRequest.CompletedAt ??= now;
        var interpreter = await _context.Users
            .FirstOrDefaultAsync(user => user.Id == userId && user.TenantId == _tenant.TenantId, cancellationToken);
        var tenant = await _context.Tenants
            .Include(currentTenant => currentTenant.OwnerUser)
            .FirstOrDefaultAsync(currentTenant => currentTenant.Id == _tenant.TenantId, cancellationToken);

        if (interpreter == null || tenant == null)
            return NotFound();

        InterpretationPdfResult generatedPdf;
        try
        {
            generatedPdf = await _interpretationPdfService.GenerateAsync(
                interpretationRequest,
                report,
                tenant,
                interpretationRequest.ImagingOrder.Client,
                interpreter,
                "he",
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed generating interpretation PDF for request {RequestId}.", requestId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "interpretation_pdf_generation_failed" });
        }

        var document = new InterpretationReportDocument
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            InterpretationRequestId = interpretationRequest.Id,
            InterpretationReportId = report.Id,
            Version = 1,
            Content = generatedPdf.Bytes,
            ContentType = "application/pdf",
            FileName = $"interpretation-{ToSafeFileNamePart(interpretationRequest.ImagingOrder.AccessionNumber)}.pdf",
            FileSize = generatedPdf.Bytes.LongLength,
            Sha256 = generatedPdf.Sha256,
            CreatedAt = now,
            CreatedByUserId = userId
        };

        _context.InterpretationReportDocuments.Add(document);
        interpretationRequest.Status = InterpretationRequestStatuses.Completed;
        interpretationRequest.StartedAt ??= now;
        interpretationRequest.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            requestId = interpretationRequest.Id,
            status = interpretationRequest.Status,
            startedAt = interpretationRequest.StartedAt,
            completedAt = interpretationRequest.CompletedAt,
            documentId = document.Id,
            documentVersion = document.Version
        });
    }

    private static string ToSafeFileNamePart(string? value)
    {
        var safeValue = new string((value ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
            .ToArray());

        return string.IsNullOrWhiteSpace(safeValue) ? "report" : safeValue;
    }

    [HttpGet("{requestId:guid}/referral")]
    public async Task<IActionResult> GetReferral(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var referral = await AssignedRequests(userId)
            .Where(r => r.Id == requestId)
            .SelectMany(r => r.ImagingOrder.Documents
                .Where(d =>
                    d.DocumentType == ImagingOrderDocumentTypes.Referral &&
                    !d.IsDeleted))
            .Select(d => new
            {
                d.OriginalFileName,
                d.ContentType,
                d.FileData
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (referral == null)
            return NotFound();

        return File(
            referral.FileData,
            referral.ContentType,
            referral.OriginalFileName);
    }
    [HttpGet("{requestId:guid}/instances/{instanceId:guid}/file")]
    public async Task<IActionResult> GetInstanceFile(
        Guid requestId,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var assignedStudyId = await AssignedRequests(userId)
            .Where(r => r.Id == requestId)
            .Select(r => (Guid?)r.ImagingStudyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!assignedStudyId.HasValue)
            return NotFound();

        var instance = await _context.ImagingInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i =>
                i.TenantId == _tenant.TenantId &&
                i.Id == instanceId &&
                i.ImagingStudyId == assignedStudyId.Value,
                cancellationToken);

        if (instance == null)
            return NotFound();

        if (!string.Equals(
                instance.StorageStatus,
                ImagingStudyStorageStatuses.LocalAndS3,
                StringComparison.Ordinal))
        {
            return Conflict(new
            {
                error = "imaging_instance_not_available",
                message = "Imaging instance is not yet available for secure retrieval."
            });
        }

        if (string.IsNullOrWhiteSpace(instance.S3Bucket) ||
            string.IsNullOrWhiteSpace(instance.S3Key))
        {
            return Conflict(new
            {
                error = "imaging_instance_s3_metadata_missing",
                message = "Imaging instance does not have S3 metadata yet."
            });
        }

        var bucket = instance.S3Bucket.Trim();
        var key = instance.S3Key.Trim();

        try
        {
            var response = await _s3Client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                },
                cancellationToken);

            var contentType =
                response.Headers.ContentType ??
                "application/dicom";

            var fileName =
                Path.GetFileName(key) ??
                $"instance-{instance.Id}.dcm";

            return File(
                response.ResponseStream,
                contentType,
                fileName);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(
                ex,
                "Interpreter S3 read failed for request {RequestId}, instance {InstanceId}.",
                requestId,
                instanceId);

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    error = "s3_read_failed",
                    message = "Unable to retrieve the imaging file from storage."
                });
        }
    }

    [HttpGet("{requestId:guid}/instances/{instanceId:guid}/annotations")]
    public async Task<IActionResult> GetInstanceAnnotations(
        Guid requestId,
        Guid instanceId,
        [FromQuery] int? frameNumber,
        CancellationToken cancellationToken)
    {
        if (!TryGetInterpreter(out var userId))
            return Forbid();

        var assignedStudyId = await AssignedRequests(userId)
            .Where(request => request.Id == requestId)
            .Select(request => (Guid?)request.ImagingStudyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!assignedStudyId.HasValue)
            return NotFound();

        var instanceExists = await _context.ImagingInstances
            .AsNoTracking()
            .AnyAsync(instance =>
                instance.TenantId == _tenant.TenantId &&
                instance.Id == instanceId &&
                instance.ImagingStudyId == assignedStudyId.Value,
                cancellationToken);

        if (!instanceExists)
            return NotFound();

        var annotations = _context.ImagingAnnotations
            .AsNoTracking()
            .Where(annotation =>
                annotation.TenantId == _tenant.TenantId &&
                annotation.ImagingInstanceId == instanceId);

        if (frameNumber.HasValue)
            annotations = annotations.Where(annotation => annotation.FrameNumber == frameNumber.Value);

        var storedAnnotations = await annotations
            .OrderBy(annotation => annotation.FrameNumber)
            .ThenBy(annotation => annotation.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(storedAnnotations.Select(annotation => new ImagingAnnotationDto(
            annotation.Id,
            annotation.ImagingInstanceId,
            annotation.FrameNumber,
            annotation.AnnotationUid,
            annotation.ToolName,
            JsonDocument.Parse(annotation.Geometry).RootElement,
            annotation.Label,
            annotation.CreatedAt,
            annotation.UpdatedAt,
            null,
            null)));
    }

    private IQueryable<InterpretationRequest> AssignedRequests(Guid userId)
    {
        return _context.InterpretationRequests
            .AsNoTracking()
            .Where(r =>
                r.TenantId == _tenant.TenantId &&
                r.AssignedInterpreterId == userId);
    }

    private bool TryGetInterpreter(out Guid userId)
    {
        userId = Guid.Empty;

        if (_tenant.TenantId == Guid.Empty ||
            !_tenant.UserId.HasValue)
            return false;

        if (!string.Equals(
                User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
                "Interpreter",
                StringComparison.OrdinalIgnoreCase))
            return false;

        userId = _tenant.UserId.Value;
        return true;
    }
}







