namespace Clienta.Api.DTOs;

public record RegisterImagingStudyRequest(
    string AccessionNumber,
    string StudyInstanceUID,
    string Modality,
    string LocalStoragePath,
    string SeriesInstanceUID,
    string SOPInstanceUID,
    string SOPClassUID,
    int? SeriesNumber,
    string? SeriesDescription,
    int? InstanceNumber,
    string LocalFilePath,
    long? FileSizeBytes
);

public record RegisterImagingStudyResponse(
    Guid ImagingStudyId,
    Guid ImagingSeriesId,
    Guid ImagingInstanceId,
    Guid TenantId,
    Guid ClientId,
    Guid? ImagingOrderId,
    string AccessionNumber,
    string StudyInstanceUID,
    string SeriesInstanceUID,
    string SOPInstanceUID,
    string Modality,
    bool StudyCreated,
    bool SeriesCreated,
    bool InstanceCreated,
    string Status,
    string StorageStatus,
    bool Created
);
