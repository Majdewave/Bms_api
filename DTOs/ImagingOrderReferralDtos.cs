using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.DTOs;

public record UpdateImagingOrderReferralRequest(string? ReferringDoctorName);

/// <summary>Updates optional internal technician notes for an imaging order.</summary>
public record UpdateImagingOrderTechnicianNotesRequest(
    [param: MaxLength(4000)] string? TechnicianNotes);

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