namespace Clienta.Api.DTOs;

public record UpdateImagingInstanceStorageRequest(
    string StorageStatus,
    string? S3Bucket,
    string? S3Key,
    string? S3ETag,
    DateTime? S3UploadedAt,
    long? FileSizeBytes
);

public record UpdateImagingInstanceStorageResponse(
    Guid ImagingInstanceId,
    Guid ImagingStudyId,
    string StorageStatus,
    string? S3Bucket,
    string? S3Key,
    string? S3ETag,
    DateTime? S3UploadedAt,
    long? FileSizeBytes,
    string StudyStorageStatus
);
