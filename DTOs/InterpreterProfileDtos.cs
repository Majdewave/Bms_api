namespace Clienta.Api.DTOs;

public record InterpreterProfileDto(
    Guid Id,
    string Email,
    string FullName,
    string? Phone,
    string? LicenseNumber,
    bool HasStamp
);

public record UpdateInterpreterProfileRequest(
    string FullName,
    string? Phone,
    string? LicenseNumber
);