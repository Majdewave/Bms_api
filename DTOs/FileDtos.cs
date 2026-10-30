namespace Clienta.Api.DTOs;

public record FileResponse(
    Guid Id,
    string FileName,
    long FileSize,
    DateTime UploadedAt,
    string? Title = null,
    Guid? ProjectId = null,
    string? ProjectName = null,
    string? UploadedByName = null,
    // Links an archived Rivhit PDF back to its external accounting document (null for normal uploads).
    Guid? ExternalAccountingDocumentId = null
);

public record UpdateFileTitleRequest(string? Title);
