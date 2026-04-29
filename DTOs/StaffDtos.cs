namespace Clienta.Api.DTOs;

public record CreateStaffRequest(
    string Email,
    string Password,
    string FullName,
    string RoleLabel,
    List<string> Permissions,
    string Role = "Staff",
    bool UseStamp = false
);

public record UpdateStaffRequest(
    string FullName,
    string RoleLabel,
    bool IsActive,
    List<string> Permissions,
    bool UseStamp,
    string Role
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
    string? StampUrl,
    bool UseStamp
);
