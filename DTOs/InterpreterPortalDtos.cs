namespace Clienta.Api.DTOs;

public record InterpreterRequestListItemDto(
    Guid Id,
    Guid ImagingOrderId,
    Guid ImagingStudyId,
    string AccessionNumber,
    string Modality,
    Guid ClientId,
    string ClientDisplayName,
    string Status,
    DateTime RequestedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt
);

public record InterpreterCaseDto(
    Guid RequestId,
    string Status,
    DateTime RequestedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,

    Guid ClientId,
    string ClientDisplayName,

    Guid ImagingOrderId,
    string AccessionNumber,
    string Modality,
    DateTime ScheduledStartTime,
    string? ReferringDoctorName,

    Guid ImagingStudyId,
    string StudyInstanceUID,
    string StudyStatus,
    DateTime ReceivedAt,

    InterpreterReferralMetadataDto? Referral,

    IReadOnlyList<InterpreterSeriesDto> Series,

    InterpreterReportDto? Report,

    bool HasFinalPdf
);

public record InterpreterReferralMetadataDto(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize
);

public record InterpreterSeriesDto(
    Guid Id,
    string SeriesInstanceUID,
    string Modality,
    int? SeriesNumber,
    string? SeriesDescription,
    IReadOnlyList<InterpreterInstanceDto> Instances
);

public record InterpreterInstanceDto(
    Guid Id,
    string SOPInstanceUID,
    string SOPClassUID,
    int? InstanceNumber,
    long? FileSizeBytes
);

public record InterpreterReportDto(
    Guid Id,
    string Content,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record SaveInterpreterReportRequest(
    string Content
);
