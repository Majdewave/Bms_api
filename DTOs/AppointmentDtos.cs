namespace Clienta.Api.DTOs;

public record CreateAppointmentRequest(
    Guid ClientId,
    DateTime StartTime,
    DateTime EndTime,
    string? Notes,
    Guid? ServiceId,
    Guid? StaffId
);

public record UpdateAppointmentRequest(
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    string? Notes,
    Guid? ServiceId,
    Guid? StaffId
);


public record AppointmentDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    Guid? ServiceId,
    string? ServiceName,
    Guid? StaffId,
    string? StaffName,
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    string? Notes,
    DateTime CreatedAt
);

public record AppointmentResponse(
    Guid Id,
    Guid ClientId,
    string ClientName,
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    Guid? ServiceId,
    Guid? StaffId,
    string? StaffName,
    string? ServiceName
);
