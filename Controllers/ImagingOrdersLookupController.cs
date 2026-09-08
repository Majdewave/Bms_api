using Amazon.S3;
using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging/orders")]
public class ImagingOrdersLookupController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<ImagingOrdersLookupController> _logger;

    public ImagingOrdersLookupController(
        AppDbContext db,
        ITenantContext tenantContext,
        IAmazonS3 s3Client,
        ILogger<ImagingOrdersLookupController> logger)
         {
        _db = db;
        _tenantContext = tenantContext;
        _s3Client = s3Client;
       _logger = logger;
         }

    [HttpGet("by-accession/{accessionNumber}")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.ImagingGatewayScheme, Policy = PlatformAuthConstants.PolicyImagingGateway)]
    public async Task<IActionResult> GetByAccession(string accessionNumber, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved.");

        if (string.IsNullOrWhiteSpace(accessionNumber))
            return BadRequest("accessionNumber is required.");

        var normalizedAccession = accessionNumber.Trim().ToUpperInvariant();

        var order = await _db.ImagingOrders
            .AsNoTracking()
            .Where(io => io.TenantId == _tenantContext.TenantId)
            .Where(io => io.AccessionNumber == normalizedAccession)
            .Select(io => new ImagingOrderByAccessionDto(
                io.Id,
                io.AccessionNumber,
                io.Modality,
                io.Status,
                io.ClientId,
                io.AppointmentId,
                io.ServiceId
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (order == null)
            return NotFound();

        return Ok(order);
    }

    [HttpDelete("{orderId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteImagingOrder(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved.");

        var tenantId = _tenantContext.TenantId;
        var s3ObjectsToDelete = new List<(string Bucket, string Key)>();

        var strategy = _db.Database.CreateExecutionStrategy();

        var found = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var order = await _db.ImagingOrders
                    .FirstOrDefaultAsync(
                        io => io.Id == orderId && io.TenantId == tenantId,
                        cancellationToken);

                if (order == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                var interpretationRequest = await _db.InterpretationRequests
                    .FirstOrDefaultAsync(
                        request =>
                            request.ImagingOrderId == orderId &&
                            request.TenantId == tenantId,
                        cancellationToken);

                if (interpretationRequest != null)
                {
                    var reportDocuments = await _db.InterpretationReportDocuments
                        .Where(document =>
                            document.InterpretationRequestId == interpretationRequest.Id &&
                            document.TenantId == tenantId)
                        .ToListAsync(cancellationToken);

                    if (reportDocuments.Count > 0)
                        _db.InterpretationReportDocuments.RemoveRange(reportDocuments);

                    var report = await _db.InterpretationReports
                        .FirstOrDefaultAsync(
                            report =>
                                report.InterpretationRequestId == interpretationRequest.Id &&
                                report.TenantId == tenantId,
                            cancellationToken);

                    if (report != null)
                        _db.InterpretationReports.Remove(report);

                    _db.InterpretationRequests.Remove(interpretationRequest);

                    await _db.SaveChangesAsync(cancellationToken);
                }

                var studies = await _db.ImagingStudies
                    .Where(study =>
                        study.ImagingOrderId == orderId &&
                        study.TenantId == tenantId)
                    .ToListAsync(cancellationToken);

                foreach (var study in studies)
                {
                    var studyId = study.Id;

                    var instanceIds = await _db.ImagingInstances
                        .Where(instance =>
                            instance.ImagingStudyId == studyId &&
                            instance.TenantId == tenantId)
                        .Select(instance => instance.Id)
                        .ToListAsync(cancellationToken);

                    if (instanceIds.Count > 0)
                    {
                        var annotations = await _db.ImagingAnnotations
                            .Where(annotation =>
                                annotation.TenantId == tenantId &&
                                instanceIds.Contains(annotation.ImagingInstanceId))
                            .ToListAsync(cancellationToken);

                        if (annotations.Count > 0)
                            _db.ImagingAnnotations.RemoveRange(annotations);

                        await _db.SaveChangesAsync(cancellationToken);
                    }

                    var instances = await _db.ImagingInstances
                        .Where(instance =>
                            instance.ImagingStudyId == studyId &&
                            instance.TenantId == tenantId)
                        .ToListAsync(cancellationToken);

                    foreach (var instance in instances)
                    {
                        if (!string.IsNullOrWhiteSpace(instance.S3Bucket) &&
                            !string.IsNullOrWhiteSpace(instance.S3Key))
                        {
                            s3ObjectsToDelete.Add((
                                instance.S3Bucket.Trim(),
                                instance.S3Key.Trim()
                            ));
                        }
                    }

                    if (instances.Count > 0)
                        _db.ImagingInstances.RemoveRange(instances);

                    await _db.SaveChangesAsync(cancellationToken);

                    var series = await _db.ImagingSeries
                        .Where(series =>
                            series.ImagingStudyId == studyId &&
                            series.TenantId == tenantId)
                        .ToListAsync(cancellationToken);

                    if (series.Count > 0)
                        _db.ImagingSeries.RemoveRange(series);

                    _db.ImagingStudies.Remove(study);

                    await _db.SaveChangesAsync(cancellationToken);
                }

                // ImagingOrderDocuments (including Referral) cascade from ImagingOrder.
                // Appointment and Client are deliberately NOT deleted.
                _db.ImagingOrders.Remove(order);
                await _db.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return true;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Ignore rollback failure and preserve the original exception.
                }

                throw;
            }
        });

        if (!found)
            return NotFound();

        // Database deletion succeeded.
        // Delete the physical DICOM objects only after the DB transaction committed.
        foreach (var s3Object in s3ObjectsToDelete.Distinct())
        {
            try
            {
                await _s3Client.DeleteObjectAsync(
                    s3Object.Bucket,
                    s3Object.Key,
                    cancellationToken);

                _logger.LogInformation(
                    "IMAGING_OBJECT_DELETED_FROM_S3 tenantId={TenantId} orderId={OrderId} bucket={Bucket} key={Key}",
                    tenantId,
                    orderId,
                    s3Object.Bucket,
                    s3Object.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "IMAGING_OBJECT_S3_DELETE_FAILED tenantId={TenantId} orderId={OrderId} bucket={Bucket} key={Key}",
                    tenantId,
                    orderId,
                    s3Object.Bucket,
                    s3Object.Key);
            }
        }

        return NoContent();
    }
}
