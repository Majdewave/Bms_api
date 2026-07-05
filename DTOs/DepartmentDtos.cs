namespace Clienta.Api.DTOs;

public record DepartmentResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    string? Color,
    int DisplayOrder,
    bool IsActive,
    DateTime CreatedAt
);

public record CreateDepartmentRequest(
    string Name,
    string? Description,
    string? Color,
    int DisplayOrder = 0
);

public record UpdateDepartmentRequest(
    string Name,
    string? Description,
    string? Color,
    int DisplayOrder,
    bool IsActive
);