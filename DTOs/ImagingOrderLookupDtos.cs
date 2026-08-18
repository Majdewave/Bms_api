namespace Clienta.Api.DTOs;

public record ImagingOrderByAccessionDto(
    Guid ImagingOrderId,
    string AccessionNumber,
    string Modality,
    string Status,
    Guid ClientId,
    Guid AppointmentId,
    Guid? ServiceId
);
