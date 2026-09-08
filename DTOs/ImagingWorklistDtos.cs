namespace Clienta.Api.DTOs;

public record ImagingWorklistItemDto(
    Guid ImagingOrderId,
    string AccessionNumber,
    string Modality,
    DateTime ScheduledStartTime,
    string Status,
    Guid ClientId,
    string FullName,
    string? IdNumber,
    DateTime? BirthDate,
    string? Phone,
    Guid? AppointmentId,
    Guid? ServiceId,
    string? ServiceName
);
