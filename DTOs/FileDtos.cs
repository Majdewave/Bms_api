namespace Clienta.Api.DTOs;

public record FileResponse(
    Guid Id,
    string FileName,
    long FileSize,
    DateTime UploadedAt
);
