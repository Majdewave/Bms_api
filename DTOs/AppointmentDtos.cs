namespace Clienta.Api.DTOs;

public record CreateAppointmentRequest(
    Guid ClientId,
    DateTime StartTime,
    DateTime EndTime,
    string? Notes,
    Guid? ServiceId,
    Guid? StaffId,
    bool IsDocumented = true,
    string? ReferringDoctorName = null
);

public record UpdateAppointmentRequest(
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    string? Notes,
    Guid? ServiceId,
    Guid? StaffId,
    bool? IsDocumented = true,
    int? QueueNumber = null,
    string? ReferringDoctorName = null
);

public record AppointmentDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    Guid? ServiceId,
    string? ServiceName,
    Guid? DepartmentId,
    string? DepartmentName,
    string? DepartmentColor,
    Guid? StaffId,
    string? StaffName,
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    int? QueueNumber,
    string? Notes,
    DateTime CreatedAt,
    bool IsDocumented,
    bool HasSignedConsent,
    Guid? ImagingOrderId
);

public record ReorderWaitingQueueItemRequest(
    Guid Id,
    int QueueNumber
);

public record ReorderWaitingQueueRequest(
    List<ReorderWaitingQueueItemRequest> Items
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
    Guid? DepartmentId,
    string? DepartmentName,
    string? DepartmentColor,
    Guid? StaffId,
    string? StaffName,
    string? ServiceName,
    bool IsDocumented
);
