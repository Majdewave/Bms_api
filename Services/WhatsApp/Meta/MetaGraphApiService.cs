using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Clienta.Api.Services.WhatsApp.Meta;

public class MetaGraphApiService : IMetaGraphApiService
{
    private const int MaxAttempts = 3;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaWhatsAppOptions _options;

    public MetaGraphApiService(IHttpClientFactory httpClientFactory, IOptions<MetaWhatsAppOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        var endpoint = $"https://graph.facebook.com/{_options.GraphApiVersion}/me?fields=id&access_token={Uri.EscapeDataString(accessToken)}";

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
                    return false;
                }

                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    return root.TryGetProperty("id", out var idEl) && !string.IsNullOrWhiteSpace(idEl.GetString());
                }
                catch (JsonException)
                {
                    return false;
                }
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return false;
            }
            catch (HttpRequestException)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return false;
            }
        }

        return false;
    }

    public async Task<(bool Found, string? Name)> GetWabaAsync(string wabaId, string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return (false, null);
        }

        var endpoint = $"https://graph.facebook.com/{_options.GraphApiVersion}/{wabaId}?fields=id,name&access_token={Uri.EscapeDataString(accessToken)}";
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

                    return (false, null);
                }

                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var found = root.TryGetProperty("id", out var idEl) && !string.IsNullOrWhiteSpace(idEl.GetString());
                var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                return (found, name);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return (false, null);
            }
            catch (HttpRequestException)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return (false, null);
            }
            catch (JsonException)
            {
                return (false, null);
            }
        }

        return (false, null);
    }

    public async Task<(bool Found, string? DisplayPhoneNumber)> GetPhoneNumberAsync(string phoneNumberId, string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return (false, null);
        }

        var endpoint = $"https://graph.facebook.com/{_options.GraphApiVersion}/{phoneNumberId}?fields=id,display_phone_number&access_token={Uri.EscapeDataString(accessToken)}";
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

                    return (false, null);
                }

                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var found = root.TryGetProperty("id", out var idEl) && !string.IsNullOrWhiteSpace(idEl.GetString());
                var displayPhone = root.TryGetProperty("display_phone_number", out var displayEl) ? displayEl.GetString() : null;
                return (found, displayPhone);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return (false, null);
            }
            catch (HttpRequestException)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return (false, null);
            }
            catch (JsonException)
            {
                return (false, null);
            }
        }

        return (false, null);
    }

    public async Task<bool> VerifyWebhookAsync(string businessAccountId, string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(businessAccountId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        var endpoint = $"https://graph.facebook.com/{_options.GraphApiVersion}/{businessAccountId}/subscribed_apps";
        var http = _httpClientFactory.CreateClient("MetaGraph");
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var body = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["access_token"] = accessToken,
                });

                using var response = await http.PostAsync(endpoint, body, cancellationToken);
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var retryable = IsRetryableStatus(response.StatusCode);
                    if (retryable && attempt < MaxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                        continue;
                    }

                    return false;
                }

                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                return root.TryGetProperty("success", out var successEl) && successEl.ValueKind == JsonValueKind.True;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return false;
            }
            catch (HttpRequestException)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return false;
    }

    public async Task<bool> IsWebhookSubscribedAsync(string businessAccountId, string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(businessAccountId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        var endpoint = $"https://graph.facebook.com/{_options.GraphApiVersion}/{businessAccountId}/subscribed_apps?access_token={Uri.EscapeDataString(accessToken)}";
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

                    return false;
                }

                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                return dataEl.GetArrayLength() > 0;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return false;
            }
            catch (HttpRequestException)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsRetryableStatus(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code == 408 || code == 429 || code >= 500;
    }
}
