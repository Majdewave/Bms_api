namespace Clienta.Api.DTOs;

public record CreateClientRequest(
    string FullName,
    string? IdNumber,
    DateTime? BirthDate,
    string? Email,
    string? Phone,
    string? Address,
    string? InternalNote,
    string? HealthFund = null,
    string? HealthFundOther = null
);

public record UpdateClientRequest(
    string FullName,
    string? IdNumber,
    DateTime? BirthDate,
    string? Email,
    string? Phone,
    string? Address,
    string? InternalNote,
    bool IsActive,
    string? HealthFund = null,
    string? HealthFundOther = null
);

public record ClientResponse(
    Guid Id,
    string FullName,
    string? IdNumber,
    DateTime? BirthDate,
    string? Email,
    string? Phone,
    string? Address,
    string? InternalNote,
    bool IsActive,
    DateTime CreatedAt,
    string Status,
    DateTime? LastVisit,
    bool IsNotDocumented,
    string? HealthFund = null,
    string? HealthFundOther = null
);

public record DuplicateClientIdNumberCheckResponse(
    bool Exists,
    Guid? ClientId,
    string? ClientName
);

