namespace Clienta.Api.DTOs;

public record InterpretationReportDto(
    Guid Id,
    string Content,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record CreateInterpretationRequestRequest(Guid AssignedInterpreterId, Guid ImagingStudyId);

public record InterpretationRequestDto(
    Guid Id,
    Guid ImagingOrderId,
    Guid ImagingStudyId,
    Guid AssignedInterpreterId,
    string AssignedInterpreterName,
    InterpretationReportDto? Report,
    string Status,
    DateTime RequestedAt,
    Guid RequestedByUserId,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    bool HasFinalPdf
);

public record InterpretationRequestListItemDto(
    Guid Id,
    Guid ImagingOrderId,
    Guid ImagingStudyId,
    string AccessionNumber,
    string Modality,
    Guid ClientId,
    string ClientDisplayName,
    Guid AssignedInterpreterId,
    string AssignedInterpreterName,
    string Status,
    DateTime RequestedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt
);