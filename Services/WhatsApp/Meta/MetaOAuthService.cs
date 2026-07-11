using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Clienta.Api.Services.WhatsApp.Meta;

public class MetaOAuthService : IMetaOAuthService
{
    private const int MaxAttempts = 3;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaWhatsAppOptions _options;

    public MetaOAuthService(IHttpClientFactory httpClientFactory, IOptions<MetaWhatsAppOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public string BuildAuthorizationUrl(string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.AppId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["state"] = state,
        };

        if (!string.IsNullOrWhiteSpace(_options.EmbeddedSignupConfigId))
        {
            query["config_id"] = _options.EmbeddedSignupConfigId;
        }
        else
        {
            query["scope"] = _options.Scopes;
        }

        return QueryHelpers.AddQueryString(
            "https://www.facebook.com/dialog/oauth",
            query);
    }

    public async Task<MetaTokenExchangeResult> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.AppId,
            ["client_secret"] = _options.AppSecret,
            ["redirect_uri"] = _options.RedirectUri,
            ["code"] = code,
        };

        var endpoint = QueryHelpers.AddQueryString($"https://graph.facebook.com/{_options.GraphApiVersion}/oauth/access_token", query);
        var http = _httpClientFactory.CreateClient("MetaGraph");
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(endpoint, cancellationToken);
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var retryable = IsRetryableStatus(response.StatusCode);
                    if (retryable && attempt < MaxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                        continue;
                    }

                    throw new MetaApiException(
                        userMessage: "Could not complete Meta token exchange.",
                        auditDetail: $"Token exchange failed with status {(int)response.StatusCode}.",
                        isTransient: retryable);
                }

                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("access_token", out var accessTokenEl) || string.IsNullOrWhiteSpace(accessTokenEl.GetString()))
                    {
                        throw new MetaApiException(
                            userMessage: "Meta token response is invalid.",
                            auditDetail: "Token exchange response missing access_token.");
                    }

                    int? expiresIn = null;
                    if (root.TryGetProperty("expires_in", out var expiresEl) && expiresEl.TryGetInt32(out var seconds))
                    {
                        expiresIn = seconds;
                    }

                    return new MetaTokenExchangeResult(accessTokenEl.GetString()!, expiresIn);
                }
                catch (JsonException)
                {
                    throw new MetaApiException(
                        userMessage: "Meta returned an invalid token response.",
                        auditDetail: "Invalid JSON in token exchange response.");
                }
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                throw new MetaApiException(
                    userMessage: "Meta token exchange timed out.",
                    auditDetail: "Token exchange request timed out.",
                    isTransient: true);
            }
            catch (HttpRequestException)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                throw new MetaApiException(
                    userMessage: "Could not reach Meta for token exchange.",
                    auditDetail: "Network error during token exchange.",
                    isTransient: true);
            }
        }

        throw new MetaApiException(
            userMessage: "Could not complete Meta token exchange.",
            auditDetail: "Token exchange failed after retries.",
            isTransient: true);
    }

    private static bool IsRetryableStatus(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code == 408 || code == 429 || code >= 500;
    }
}
