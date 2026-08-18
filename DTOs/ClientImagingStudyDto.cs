namespace Clienta.Api.DTOs;

public record ClientImagingStudyDto(
    Guid Id,
    Guid? ImagingOrderId,
    string AccessionNumber,
    string StudyInstanceUID,
    string Modality,
    string Status,
    DateTime ReceivedAt,
    string StorageStatus,
    DateTime CreatedAt
);
