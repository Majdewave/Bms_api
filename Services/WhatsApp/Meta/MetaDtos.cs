namespace Clienta.Api.Services.WhatsApp.Meta;

public record MetaStatePayload(Guid TenantId, Guid UserId, string Nonce, DateTime IssuedAtUtc);

public record MetaTokenExchangeResult(string AccessToken, int? ExpiresInSeconds);

public record MetaBusinessProfile(
    string BusinessAccountId,
    string BusinessName,
    string PhoneNumberId,
    string DisplayPhoneNumber
);