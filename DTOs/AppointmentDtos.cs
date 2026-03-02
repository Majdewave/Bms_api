namespace Clienta.Api.DTOs;

public record CreateAppointmentRequest(
    Guid ClientId,
    DateTime StartTime,
    DateTime EndTime,
    string? Notes,
    Guid? ServiceId // New field for service
);

public record UpdateAppointmentRequest(
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    string? Notes
);

public record AppointmentResponse(
    Guid Id,
    Guid ClientId,
    string ClientName,
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    string? Notes,
    DateTime CreatedAt
);
