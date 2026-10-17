namespace Clienta.Api.DTOs;

public record CreateClientContactRequest(
    string Name,
    string? RoleTitle,
    string? Phone,
    string? WhatsApp,
    string? Email,
    bool IsPrimary,
    bool IsBillingContact
);

public record UpdateClientContactRequest(
    string Name,
    string? RoleTitle,
    string? Phone,
    string? WhatsApp,
    string? Email,
    bool IsPrimary,
    bool IsBillingContact
);

public record ClientContactResponse(
    Guid Id,
    Guid ClientId,
    string Name,
    string? RoleTitle,
    string? Phone,
    string? WhatsApp,
    string? Email,
    bool IsPrimary,
    bool IsBillingContact,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
