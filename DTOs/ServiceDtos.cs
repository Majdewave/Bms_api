namespace Clienta.Api.DTOs;

public record ServiceResponse(
    Guid Id,
    string Name,
    int DefaultDurationMinutes,
    Guid? DepartmentId,
    string? DepartmentName,
    string? DepartmentColor
);

public record CreateServiceRequest(
    string Name,
    int DefaultDurationMinutes,
    Guid? DepartmentId
);

public record UpdateServiceRequest(
    string Name,
    int DefaultDurationMinutes,
    Guid? DepartmentId
);