namespace Clienta.Api.DTOs;

public record CreateAppointmentRequest(
    Guid ClientId,
    DateTime StartTime,
    DateTime EndTime,
    string? Notes,
    Guid? ServiceId,
    Guid? StaffId,
    bool IsDocumented = true
);

public record UpdateAppointmentRequest(
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    string? Notes,
    Guid? ServiceId,
    Guid? StaffId,
    bool? IsDocumented = true
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
    DateTime CreatedAt,
    bool IsDocumented
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
    string? ServiceName,
    bool IsDocumented
);
