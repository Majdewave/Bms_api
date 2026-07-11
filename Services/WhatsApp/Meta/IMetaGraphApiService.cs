namespace Clienta.Api.Services.WhatsApp.Meta;

public interface IMetaGraphApiService
{
    Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<(bool Found, string? Name)> GetWabaAsync(string wabaId, string accessToken, CancellationToken cancellationToken = default);
    Task<(bool Found, string? DisplayPhoneNumber)> GetPhoneNumberAsync(string phoneNumberId, string accessToken, CancellationToken cancellationToken = default);
    Task<bool> VerifyWebhookAsync(string businessAccountId, string accessToken, CancellationToken cancellationToken = default);
    Task<bool> IsWebhookSubscribedAsync(string businessAccountId, string accessToken, CancellationToken cancellationToken = default);
}
