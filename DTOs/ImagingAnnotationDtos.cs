using System.Text.Json;

namespace Clienta.Api.DTOs;

public record CreateImagingAnnotationRequest(
    Guid ImagingInstanceId,
    int FrameNumber,
    string AnnotationUid,
    string ToolName,
    JsonElement Geometry,
    string? Label
);

public record ImagingAnnotationDto(
    Guid Id,
    Guid ImagingInstanceId,
    int FrameNumber,
    string AnnotationUid,
    string ToolName,
    JsonElement Geometry,
    string? Label,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? CreatedByUserId,
    Guid? UpdatedByUserId
);

public record UpdateImagingAnnotationRequest(
    JsonElement Geometry,
    string? Label
);
