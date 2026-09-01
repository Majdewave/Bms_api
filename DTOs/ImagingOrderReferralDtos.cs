namespace Clienta.Api.DTOs;

public record UpdateImagingOrderReferralRequest(string? ReferringDoctorName);

public record ImagingOrderReferralDocumentDto(
    Guid Id,
    string DocumentType,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    DateTime CreatedAt
);

public record ImagingOrderReferralDto(
    Guid ImagingOrderId,
    string? ReferringDoctorName,
    ImagingOrderReferralDocumentDto? Document
);