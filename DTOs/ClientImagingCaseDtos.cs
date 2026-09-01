namespace Clienta.Api.DTOs;

public record ClientImagingReferralDocumentDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    DateTime CreatedAt
);

public record ClientImagingCaseDto(
    Guid Id,
    Guid ClientId,
    Guid AppointmentId,
    Guid? ServiceId,
    string AccessionNumber,
    string Modality,
    string Status,
    string? ReferringDoctorName,
    DateTime ScheduledStartTime,
    DateTime CreatedAt,
    ClientImagingReferralDocumentDto? Referral,
    ClientImagingStudyDto? Study
);