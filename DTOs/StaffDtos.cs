namespace Clienta.Api.DTOs;

public record CreateStaffRequest(
    string Email,
    string? Password,
    string FullName,
    string RoleLabel,
    List<string> Permissions,
    List<Guid>? DepartmentIds,
    string Role = "Staff",
    bool UseStamp = false
);

public record UpdateStaffRequest(
    string FullName,
    string RoleLabel,
    bool IsActive,
    List<string> Permissions,
    List<Guid>? DepartmentIds,
    bool UseStamp,
    string Role,
    string? Password,
    string Email
);

public record StaffResponse(
    Guid Id, // BusinessUserId
    Guid UserId, // User.Id
    string Email,
    string FullName,
    string RoleLabel,
    string Role,
    bool IsActive,
    List<string> Permissions,
    List<Guid> DepartmentIds,
    DateTime? LastLoginAt,
    string? StampUrl,
    bool UseStamp,
    bool IsOwner
);
