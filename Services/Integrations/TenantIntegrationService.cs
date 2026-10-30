using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services.Integrations;

public sealed record IntegrationStateDto(
    string Provider,
    bool IsEnabled,
    bool IsConfigured,
    string ConnectionStatus,
    DateTime? LastConnectionTestAt,
    string? LastConnectionErrorCode,
    bool AccountingEnabled,
    bool PaymentsAvailable);

public sealed class UpdateIntegrationRequest
{
    public bool? IsEnabled { get; set; }
    public bool? AccountingEnabled { get; set; }

    // Null, blank or a masked placeholder means "keep the stored credential".
    public string? ApiToken { get; set; }
}

public enum IntegrationOutcome
{
    Ok,
    InvalidToken,
    NotConfigured,
    SecretStorageUnavailable
}

public sealed record IntegrationUpdateResult(IntegrationOutcome Outcome, IntegrationStateDto State);

public sealed record IntegrationTestResult(IntegrationOutcome Outcome, bool Success, string? FailureCode, IntegrationStateDto State);

public enum IntegrationMetadataOutcome
{
    Ok,
    NotConfigured,
    MetadataUnavailable
}

public sealed record IntegrationMetadataResult(
    IntegrationMetadataOutcome Outcome,
    IReadOnlyList<ProviderPaymentType>? PaymentTypes = null,
    IReadOnlyList<ProviderReceiptType>? ReceiptTypes = null);

public interface ITenantIntegrationService
{
    Task<IntegrationStateDto> GetStateAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct);
    Task<IntegrationUpdateResult> UpdateAsync(Guid tenantId, IntegrationProvider provider, UpdateIntegrationRequest request, CancellationToken ct);
    Task<IntegrationTestResult> TestConnectionAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct);
    Task<IntegrationMetadataResult> GetMetadataAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct);
    Task<IntegrationStateDto> DisconnectAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct);

    // Gate for future operational use (documents etc.). Null unless enabled, configured and accounting is on.
    Task<string?> GetOperationalCredentialAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct);
}

public sealed class TenantIntegrationService : ITenantIntegrationService
{
    private const int MaxTokenLength = 200;

    private readonly AppDbContext _db;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IReadOnlyDictionary<IntegrationProvider, IAccountingIntegrationProvider> _providers;

    public TenantIntegrationService(AppDbContext db, IIntegrationSecretProtector protector, IEnumerable<IAccountingIntegrationProvider> providers)
    {
        _db = db;
        _protector = protector;
        _providers = providers.ToDictionary(p => p.Provider);
    }

    public async Task<IntegrationStateDto> GetStateAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct)
    {
        var row = await FindAsync(tenantId, provider, ct, track: false);
        return ToDto(provider, row);
    }

    public async Task<IntegrationUpdateResult> UpdateAsync(Guid tenantId, IntegrationProvider provider, UpdateIntegrationRequest request, CancellationToken ct)
    {
        var row = await FindAsync(tenantId, provider, ct, track: true);
        var newToken = NormalizeToken(request.ApiToken, out var tokenValid);
        if (!tokenValid)
            return new(IntegrationOutcome.InvalidToken, ToDto(provider, row));

        var created = false;
        if (row is null)
        {
            row = new TenantIntegration { TenantId = tenantId, Provider = provider };
            created = true;
        }

        if (newToken is not null)
        {
            var protectedToken = _protector.Protect(newToken, Purpose(tenantId, provider));
            if (protectedToken is null)
                return new(IntegrationOutcome.SecretStorageUnavailable, ToDto(provider, created ? null : row));

            row.EncryptedCredential = protectedToken;
            row.ConnectionStatus = IntegrationConnectionStatus.Unknown;
            row.LastConnectionTestAt = null;
            row.LastConnectionSucceededAt = null;
            row.LastConnectionErrorCode = null;
        }

        if (request.IsEnabled == true && string.IsNullOrEmpty(row.EncryptedCredential))
            return new(IntegrationOutcome.NotConfigured, ToDto(provider, created ? null : row));

        if (request.IsEnabled.HasValue) row.IsEnabled = request.IsEnabled.Value;
        if (request.AccountingEnabled.HasValue) row.AccountingEnabled = request.AccountingEnabled.Value;
        row.PaymentsEnabled = false;
        row.UpdatedAt = DateTime.UtcNow;

        if (created) _db.TenantIntegrations.Add(row);
        await _db.SaveChangesAsync(ct);
        return new(IntegrationOutcome.Ok, ToDto(provider, row));
    }

    public async Task<IntegrationTestResult> TestConnectionAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct)
    {
        var row = await FindAsync(tenantId, provider, ct, track: true);
        if (row is null || string.IsNullOrEmpty(row.EncryptedCredential))
            return new(IntegrationOutcome.NotConfigured, false, "NotConfigured", ToDto(provider, row));

        var adapter = _providers.GetValueOrDefault(provider);
        var token = _protector.Unprotect(row.EncryptedCredential, Purpose(tenantId, provider));

        ProviderConnectionResult result;
        string? failureCode = null;
        if (adapter is null)
        {
            result = ProviderConnectionResult.Fail(ProviderConnectionFailure.ProviderUnavailable);
        }
        else if (token is null)
        {
            // Stored secret cannot be read (key missing/rotated): ask the admin to re-enter it.
            result = ProviderConnectionResult.Fail(ProviderConnectionFailure.InvalidCredentials);
            failureCode = "CredentialUnreadable";
        }
        else
        {
            try
            {
                result = await adapter.TestConnectionAsync(token, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = ProviderConnectionResult.Fail(ProviderConnectionFailure.ProviderUnavailable);
            }
        }

        var now = DateTime.UtcNow;
        row.LastConnectionTestAt = now;
        row.UpdatedAt = now;
        if (result.Success)
        {
            row.ConnectionStatus = IntegrationConnectionStatus.Connected;
            row.LastConnectionSucceededAt = now;
            row.LastConnectionErrorCode = null;
        }
        else
        {
            row.ConnectionStatus = IntegrationConnectionStatus.Failed;
            row.LastConnectionErrorCode = failureCode ?? result.Failure.ToString();
        }

        await _db.SaveChangesAsync(ct);
        return new(IntegrationOutcome.Ok, result.Success, row.LastConnectionErrorCode, ToDto(provider, row));
    }

    public async Task<IntegrationMetadataResult> GetMetadataAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct)
    {
        var credential = await GetOperationalCredentialAsync(tenantId, provider, ct);
        if (string.IsNullOrWhiteSpace(credential))
            return new(IntegrationMetadataOutcome.NotConfigured);

        if (!_providers.TryGetValue(provider, out var integrationProvider)
            || integrationProvider is not IAccountingMetadataProvider metadataProvider)
            return new(IntegrationMetadataOutcome.MetadataUnavailable);

        try
        {
            var paymentTypes = await metadataProvider.GetPaymentTypesAsync(credential, ct);
            var receiptTypes = await metadataProvider.GetReceiptTypesAsync(credential, ct);
            return new(IntegrationMetadataOutcome.Ok, paymentTypes, receiptTypes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(IntegrationMetadataOutcome.MetadataUnavailable);
        }
    }

    // Removes only the credential and switches the integration off. Row, mappings and history stay.
    public async Task<IntegrationStateDto> DisconnectAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct)
    {
        var row = await FindAsync(tenantId, provider, ct, track: true);
        if (row is null) return ToDto(provider, null);

        row.EncryptedCredential = null;
        row.IsEnabled = false;
        row.ConnectionStatus = IntegrationConnectionStatus.Unknown;
        row.LastConnectionTestAt = null;
        row.LastConnectionSucceededAt = null;
        row.LastConnectionErrorCode = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(provider, row);
    }

    public async Task<string?> GetOperationalCredentialAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct)
    {
        var row = await FindAsync(tenantId, provider, ct, track: false);
        if (row is null || !row.IsEnabled || !row.AccountingEnabled || string.IsNullOrEmpty(row.EncryptedCredential))
            return null;

        return _protector.Unprotect(row.EncryptedCredential, Purpose(tenantId, provider));
    }

    private async Task<TenantIntegration?> FindAsync(Guid tenantId, IntegrationProvider provider, CancellationToken ct, bool track)
    {
        if (tenantId == Guid.Empty) return null;

        var query = _db.TenantIntegrations.Where(i => i.TenantId == tenantId && i.Provider == provider);
        if (!track) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct);
    }

    private static string Purpose(Guid tenantId, IntegrationProvider provider) => $"integration:{provider}:{tenantId:N}";

    private static string? NormalizeToken(string? raw, out bool valid)
    {
        valid = true;
        var token = raw?.Trim();
        if (string.IsNullOrEmpty(token) || token.All(c => c is '*' or '\u2022' or '\u25CF' or '\u00B7'))
            return null;

        if (token.Length > MaxTokenLength || token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            valid = false;
            return null;
        }

        return token;
    }

    private static IntegrationStateDto ToDto(IntegrationProvider provider, TenantIntegration? row)
    {
        var configured = row is not null && !string.IsNullOrEmpty(row.EncryptedCredential);
        var status = !configured ? "notConfigured"
            : !row!.IsEnabled ? "disabled"
            : row.ConnectionStatus switch
            {
                IntegrationConnectionStatus.Connected => "connected",
                IntegrationConnectionStatus.Failed => "failed",
                _ => "notTested"
            };

        return new IntegrationStateDto(
            provider.ToString(),
            row?.IsEnabled ?? false,
            configured,
            status,
            row?.LastConnectionTestAt,
            row?.LastConnectionErrorCode,
            row?.AccountingEnabled ?? true,
            PaymentsAvailable: false);
    }
}
