namespace Clienta.Api.DTOs;

public record ImagingInstanceUploadUrlResponse(
    string UploadUrl,
    string Bucket,
    string ObjectKey,
    DateTime ExpiresAtUtc
);
