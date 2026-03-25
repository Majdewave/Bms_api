namespace Clienta.Api.DTOs;

public record CreateStaffRequest(
    string Email,
    string Password,
    string FullName,
    string RoleLabel,
    List<string> Permissions,
    string Role = "Staff"
);

public record UpdateStaffRequest(
    string FullName,
    string RoleLabel,
    bool IsActive,
    List<string> Permissions
);

public record StaffResponse(
    Guid Id,
    string Email,
    string FullName,
    string RoleLabel,
    string Role,
    bool IsActive,
    List<string> Permissions
);
