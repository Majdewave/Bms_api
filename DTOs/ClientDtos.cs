namespace Clienta.Api.DTOs;

public record CreateClientRequest(
    string FullName,
    string? Email,
    string? Phone,
    string? Address,
    string? InternalNote
);

public record UpdateClientRequest(
    string FullName,
    string? Email,
    string? Phone,
    string? Address,
    string? InternalNote,
    bool IsActive
);

public record ClientResponse(
    Guid Id,
    string FullName,
    string? Email,
    string? Phone,
    string? Address,
    string? InternalNote,
    bool IsActive,
    DateTime CreatedAt,
    string Status,
    DateTime? LastVisit
);
