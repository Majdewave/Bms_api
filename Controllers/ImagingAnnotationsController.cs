using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging")]
public class ImagingAnnotationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ImagingAnnotationsController> _logger;

    public ImagingAnnotationsController(
        AppDbContext db,
        ITenantContext tenantContext,
        ILogger<ImagingAnnotationsController> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet("instances/{imagingInstanceId:guid}/annotations")]
    [Authorize(Policy = "view_clients")]
    public async Task<IActionResult> GetAnnotations(
        Guid imagingInstanceId,
        [FromQuery] int? frameNumber,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Unauthorized(new { error = "tenant_not_resolved", message = "Tenant not resolved." });
        }

        // Verify the imaging instance belongs to the current tenant
        var instanceExists = await _db.ImagingInstances
            .AsNoTracking()
            .AnyAsync(i => i.TenantId == _tenantContext.TenantId && i.Id == imagingInstanceId, cancellationToken);

        if (!instanceExists)
        {
            return NotFound(new { error = "imaging_instance_not_found", message = "Imaging instance not found for the current tenant." });
        }

        var query = _db.ImagingAnnotations
            .AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId && a.ImagingInstanceId == imagingInstanceId);

        if (frameNumber.HasValue)
        {
            query = query.Where(a => a.FrameNumber == frameNumber.Value);
        }

        var annotations = await query
            .OrderBy(a => a.FrameNumber)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(annotations.Select(ToDto));
    }

    [HttpPost("annotations")]
    [Authorize(Policy = "manage_clients")]
    public async Task<IActionResult> CreateAnnotation(
        CreateImagingAnnotationRequest request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Unauthorized(new { error = "tenant_not_resolved", message = "Tenant not resolved." });
        }

        if (!_tenantContext.UserId.HasValue || _tenantContext.UserId.Value == Guid.Empty)
        {
            return Unauthorized(new { error = "user_not_resolved", message = "User not resolved." });
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.AnnotationUid))
        {
            return BadRequest(new { error = "validation_failed", message = "AnnotationUid is required." });
        }

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return BadRequest(new { error = "validation_failed", message = "ToolName is required." });
        }

        if (request.FrameNumber < 1)
        {
            return BadRequest(new { error = "validation_failed", message = "FrameNumber must be at least 1." });
        }

        if (request.Geometry.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return BadRequest(new { error = "validation_failed", message = "Geometry is required." });
        }

        // Verify the imaging instance belongs to the current tenant
        var instance = await _db.ImagingInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == _tenantContext.TenantId && i.Id == request.ImagingInstanceId, cancellationToken);

        if (instance == null)
        {
            return NotFound(new { error = "imaging_instance_not_found", message = "Imaging instance not found for the current tenant." });
        }

        // Check if annotation with same UID already exists for this tenant
        var existingAnnotation = await _db.ImagingAnnotations
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == _tenantContext.TenantId && a.AnnotationUid == request.AnnotationUid, cancellationToken);

        if (existingAnnotation != null)
        {
            return Conflict(new { error = "annotation_uid_exists", message = "An annotation with this UID already exists for the current tenant." });
        }

        var annotation = new ImagingAnnotation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            ImagingInstanceId = request.ImagingInstanceId,
            FrameNumber = request.FrameNumber,
            AnnotationUid = request.AnnotationUid,
            ToolName = request.ToolName,
            Geometry = request.Geometry.GetRawText(),
            Label = request.Label,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _tenantContext.UserId.Value
        };

        _db.ImagingAnnotations.Add(annotation);
        await _db.SaveChangesAsync(cancellationToken);

        var responseDto = ToDto(annotation);

        return CreatedAtAction(nameof(GetAnnotations), new { imagingInstanceId = annotation.ImagingInstanceId }, responseDto);
    }

    [HttpPut("annotations/{id:guid}")]
    [Authorize(Policy = "manage_clients")]
    public async Task<IActionResult> UpdateAnnotation(
        Guid id,
        UpdateImagingAnnotationRequest request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Unauthorized(new { error = "tenant_not_resolved", message = "Tenant not resolved." });
        }

        if (!_tenantContext.UserId.HasValue || _tenantContext.UserId.Value == Guid.Empty)
        {
            return Unauthorized(new { error = "user_not_resolved", message = "User not resolved." });
        }

        var annotation = await _db.ImagingAnnotations
            .FirstOrDefaultAsync(a => a.TenantId == _tenantContext.TenantId && a.Id == id, cancellationToken);

        if (annotation == null)
        {
            return NotFound(new { error = "annotation_not_found", message = "Annotation not found for the current tenant." });
        }

        // Update only allowed fields
        if (request.Geometry.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return BadRequest(new { error = "validation_failed", message = "Geometry is required." });
        }

        annotation.Geometry = request.Geometry.GetRawText();
        annotation.Label = request.Label;
        annotation.UpdatedAt = DateTime.UtcNow;
        annotation.UpdatedByUserId = _tenantContext.UserId.Value;

        _db.ImagingAnnotations.Update(annotation);
        await _db.SaveChangesAsync(cancellationToken);

        var responseDto = ToDto(annotation);

        return Ok(responseDto);
    }

    [HttpDelete("annotations/{id:guid}")]
    [Authorize(Policy = "manage_clients")]
    public async Task<IActionResult> DeleteAnnotation(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            return Unauthorized(new { error = "tenant_not_resolved", message = "Tenant not resolved." });
        }

        var annotation = await _db.ImagingAnnotations
            .FirstOrDefaultAsync(a => a.TenantId == _tenantContext.TenantId && a.Id == id, cancellationToken);

        if (annotation == null)
        {
            return NotFound(new { error = "annotation_not_found", message = "Annotation not found for the current tenant." });
        }

        _db.ImagingAnnotations.Remove(annotation);
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static ImagingAnnotationDto ToDto(ImagingAnnotation annotation)
    {
        using var document = JsonDocument.Parse(annotation.Geometry);
        return new ImagingAnnotationDto(
            annotation.Id,
            annotation.ImagingInstanceId,
            annotation.FrameNumber,
            annotation.AnnotationUid,
            annotation.ToolName,
            document.RootElement.Clone(),
            annotation.Label,
            annotation.CreatedAt,
            annotation.UpdatedAt,
            annotation.CreatedByUserId,
            annotation.UpdatedByUserId);
    }
}
