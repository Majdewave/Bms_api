using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clienta.Api.Entities;

namespace Clienta.Api.Services.Integrations;

public sealed class RivhitOptions
{
    public const string DefaultBaseUrl = "https://api.rivhit.co.il/online/RivhitOnlineAPI.svc/";

    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public int TimeoutSeconds { get; set; } = 15;

    public Uri ResolveBaseUri()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl.Trim();
        if (!raw.EndsWith('/')) raw += "/";
        return Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : new Uri(DefaultBaseUrl);
    }
}

public sealed partial class RivhitAccountingProvider : IAccountingDocumentProvider
{
    // Read-only: returns the company profile of the token's account.
    private const string ConnectionTestOperation = "Company.Details";

    private readonly HttpClient _http;
    private readonly ILogger<RivhitAccountingProvider> _logger;
    private readonly IHostEnvironment? _hostEnvironment;

    public RivhitAccountingProvider(HttpClient http, ILogger<RivhitAccountingProvider> logger, IHostEnvironment? hostEnvironment = null)
    {
        _http = http;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    public IntegrationProvider Provider => IntegrationProvider.Rivhit;

    public async Task<ProviderConnectionResult> TestConnectionAsync(string credential, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ConnectionTestOperation)
            {
                Content = JsonContent.Create(new Dictionary<string, string> { ["api_token"] = credential })
            };
            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ProviderConnectionResult.Fail(ProviderConnectionFailure.InvalidCredentials);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Rivhit connection test returned HTTP {Status}", (int)response.StatusCode);
                return ProviderConnectionResult.Fail(ProviderConnectionFailure.ProviderUnavailable);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("error_code", out var code)
                || code.ValueKind != JsonValueKind.Number
                || !code.TryGetInt32(out var errorCode))
            {
                return ProviderConnectionResult.Fail(ProviderConnectionFailure.InvalidResponse);
            }

            if (errorCode == 0) return ProviderConnectionResult.Ok();

            _logger.LogInformation("Rivhit connection test rejected with error_code {ErrorCode}", errorCode);
            return ProviderConnectionResult.Fail(ProviderConnectionFailure.InvalidCredentials);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderConnectionResult.Fail(ProviderConnectionFailure.Timeout);
        }
        catch (JsonException)
        {
            return ProviderConnectionResult.Fail(ProviderConnectionFailure.InvalidResponse);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Rivhit connection test failed: {Error}", ex.GetType().Name);
            return ProviderConnectionResult.Fail(ProviderConnectionFailure.ProviderUnavailable);
        }
    }
}
