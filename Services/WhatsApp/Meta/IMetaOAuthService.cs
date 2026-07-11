namespace Clienta.Api.Services.WhatsApp.Meta;

public interface IMetaOAuthService
{
    string BuildAuthorizationUrl(string state);
    Task<MetaTokenExchangeResult> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default);
}
