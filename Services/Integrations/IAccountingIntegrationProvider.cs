using Clienta.Api.Entities;

namespace Clienta.Api.Services.Integrations;

public enum ProviderConnectionFailure
{
    None = 0,
    InvalidCredentials,
    ProviderUnavailable,
    Timeout,
    InvalidResponse
}

// Sanitized outcome; carries no provider payload.
public sealed record ProviderConnectionResult(bool Success, ProviderConnectionFailure Failure)
{
    public static ProviderConnectionResult Ok() => new(true, ProviderConnectionFailure.None);
    public static ProviderConnectionResult Fail(ProviderConnectionFailure failure) => new(false, failure);
}

// Provider boundary. Phase 1 only needs the connection test; later phases add document operations here.
public interface IAccountingIntegrationProvider
{
    IntegrationProvider Provider { get; }

    Task<ProviderConnectionResult> TestConnectionAsync(string credential, CancellationToken cancellationToken);
}
