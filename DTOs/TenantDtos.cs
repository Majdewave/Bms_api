namespace Clienta.Api.DTOs;

public record TenantResponse(
    Guid Id,
    string Name,
    string Subdomain,
    string? LogoUrl,
    string Plan,
    string SubscriptionStatus,
    DateTime CreatedAt
);
