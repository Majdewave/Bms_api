using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services.WhatsApp.Meta;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services.WhatsApp;

public class WhatsAppService : IWhatsAppService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly IMetaOAuthService _metaOAuthService;
    private readonly IMetaGraphApiService _metaGraphApiService;
    private readonly IMetaStateService _metaStateService;
    private readonly MetaWhatsAppOptions _metaOptions;
    private readonly IConfiguration _configuration;

    public WhatsAppService(
        AppDbContext context,
        ITenantContext tenantContext,
        ILogger<WhatsAppService> logger,
        IMetaOAuthService metaOAuthService,
        IMetaGraphApiService metaGraphApiService,
        IMetaStateService metaStateService,
        IOptions<MetaWhatsAppOptions> metaOptions,
        IConfiguration configuration)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _metaOAuthService = metaOAuthService;
        _metaGraphApiService = metaGraphApiService;
        _metaStateService = metaStateService;
        _metaOptions = metaOptions.Value;
        _configuration = configuration;
    }

    public async Task<WhatsAppStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Tenant context was not resolved.");
        }

        var settings = await _context.WhatsAppSettings
            .Where(x => x.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is not null)
        {
            await FinalizeStaleConnectingStateAsync(settings, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var messagesThisMonth = await _context.WhatsAppMessages
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.CreatedAt >= monthStart)
            .CountAsync(cancellationToken);

        var templatesCount = await _context.WhatsAppTemplates
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .CountAsync(cancellationToken);

        var conversationsCount = await _context.WhatsAppMessages
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.CreatedAt >= monthStart)
            .Select(m => m.PhoneNumber)
            .Distinct()
            .CountAsync(cancellationToken);

        var lastMessageActivity = await _context.WhatsAppMessages
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => (DateTime?)m.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var effectiveConnectionStatus = ResolveEffectiveConnectionStatus(settings);
        var capabilities = BuildCapabilities(settings);

        return new WhatsAppStatusDto(
            ConnectionStatus: MapStatus(effectiveConnectionStatus),
            Connected: effectiveConnectionStatus == WhatsAppConnectionStatus.Connected,
            BusinessName: settings?.BusinessName,
            PhoneNumber: settings?.DisplayPhoneNumber,
            ConnectedSince: settings?.ConnectedSince,
            WebhookVerified: settings?.WebhookVerified ?? false,
            WebhookVerifiedAt: settings?.WebhookVerifiedAt,
            GraphApiVersion: settings?.GraphApiVersion,
            LastError: settings?.LastError,
            LastErrorAt: settings?.LastErrorAt,
            MessagesThisMonth: messagesThisMonth,
            TemplatesCount: templatesCount,
            ConversationsCount: conversationsCount,
            LastActivity: settings?.LastActivity ?? lastMessageActivity,
            Capabilities: capabilities
        );
    }

    public async Task<WhatsAppConnectionDiagnosticsDto> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<WhatsAppDiagnosticsCheckDto>();
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Tenant context was not resolved.");
        }

        var settings = await _context.WhatsAppSettings
            .Where(x => x.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);

        string connectionStatus = "Disconnected";
        string? graphApiVersion = settings?.GraphApiVersion;
        string? businessName = settings?.BusinessName;
        string? businessAccountId = settings?.BusinessAccountId;
        string? phoneNumber = settings?.DisplayPhoneNumber;
        string? phoneNumberId = settings?.PhoneNumberId;
        var webhookVerified = settings?.WebhookVerified ?? false;
        var lastWebhookReceivedAt = settings?.LastWebhookReceivedAt;
        var lastError = settings?.LastError;

        var configurationValid = false;
        var accessTokenValid = false;
        var businessFound = false;
        var phoneFound = false;

        string? decryptedAccessToken = null;
        string? liveBusinessName = null;
        string? liveDisplayPhoneNumber = null;

        await AddCheckAsync(checks, "Configuration", () =>
        {
            if (settings is null)
            {
                return (false, "Tenant has no WhatsApp settings.");
            }

            configurationValid =
                !string.IsNullOrWhiteSpace(settings.GraphApiVersion) &&
                !string.IsNullOrWhiteSpace(settings.BusinessAccountId) &&
                !string.IsNullOrWhiteSpace(settings.PhoneNumberId);

            return configurationValid
                ? (true, "WhatsApp configuration exists.")
                : (false, "Configuration is incomplete in stored settings.");
        });

        await AddCheckAsync(checks, "Connection Status", () =>
        {
            if (settings is null)
            {
                return (false, "No stored WhatsApp connection status.");
            }

            var resolved = ResolveEffectiveConnectionStatus(settings);
            connectionStatus = MapStatus(resolved);

            var ok = resolved == WhatsAppConnectionStatus.Connected || resolved == WhatsAppConnectionStatus.TokenExpired;
            return ok
                ? (true, $"Status is {connectionStatus}.")
                : (false, $"Status is {connectionStatus}.");
        });

        await AddCheckAsync(checks, "Access Token Decryption", async () =>
        {
            if (settings is null)
            {
                return (false, "No WhatsApp settings found.");
            }

            if (string.IsNullOrWhiteSpace(settings.AccessToken))
            {
                return (false, "No access token is stored.");
            }

            var encrypted = await ReadEncryptedAccessTokenAsync(tenantId, cancellationToken);
            if (string.IsNullOrWhiteSpace(encrypted))
            {
                return (false, "Encrypted access token could not be read.");
            }

            decryptedAccessToken = WhatsAppTokenProtector.Decrypt(encrypted);
            if (string.IsNullOrWhiteSpace(decryptedAccessToken))
            {
                return (false, "Access token decryption failed.");
            }

            return (true, "Access token decrypted successfully.");
        });

        await AddCheckAsync(checks, "Graph API", async () =>
        {
            if (string.IsNullOrWhiteSpace(decryptedAccessToken))
            {
                return (false, "Skipped: access token is unavailable.");
            }

            var valid = await _metaGraphApiService.ValidateAccessTokenAsync(decryptedAccessToken, cancellationToken);
            accessTokenValid = valid;
            if (!valid)
            {
                lastError = "Access token is invalid or expired.";
                return (false, "Graph API rejected the access token.");
            }

            return (true, "Graph API responded successfully.");
        });

        await AddCheckAsync(checks, "Access Token", () =>
        {
            return accessTokenValid
                ? (true, "Access token is valid.")
                : (false, "Access token is invalid or expired.");
        });

        await AddCheckAsync(checks, "Business Account", async () =>
        {
            if (!accessTokenValid || string.IsNullOrWhiteSpace(decryptedAccessToken) || settings is null)
            {
                return (false, "Skipped: token or settings are unavailable.");
            }

            if (string.IsNullOrWhiteSpace(settings.BusinessAccountId))
            {
                return (false, "Stored WABA ID is missing.");
            }

            var waba = await _metaGraphApiService.GetWabaAsync(settings.BusinessAccountId, decryptedAccessToken, cancellationToken);
            businessFound = waba.Found;
            businessAccountId = settings.BusinessAccountId;
            liveBusinessName = waba.Name;
            businessName = !string.IsNullOrWhiteSpace(waba.Name) ? waba.Name : settings.BusinessName;

            return businessFound
                ? (true, "Business account found.")
                : (false, "Business account not found.");
        });

        await AddCheckAsync(checks, "Phone Number", async () =>
        {
            if (!accessTokenValid || string.IsNullOrWhiteSpace(decryptedAccessToken) || settings is null)
            {
                return (false, "Skipped: token or settings are unavailable.");
            }

            if (string.IsNullOrWhiteSpace(settings.PhoneNumberId))
            {
                return (false, "Stored phone number ID is missing.");
            }

            var phone = await _metaGraphApiService.GetPhoneNumberAsync(settings.PhoneNumberId, decryptedAccessToken, cancellationToken);
            phoneFound = phone.Found;
            phoneNumberId = settings.PhoneNumberId;
            liveDisplayPhoneNumber = phone.DisplayPhoneNumber;
            phoneNumber = !string.IsNullOrWhiteSpace(phone.DisplayPhoneNumber) ? phone.DisplayPhoneNumber : settings.DisplayPhoneNumber;

            return phoneFound
                ? (true, "Phone number found.")
                : (false, "Phone number not found.");
        });

        await AddCheckAsync(checks, "Display Phone Number", () =>
        {
            if (!phoneFound)
            {
                return (false, "Skipped: phone number lookup failed.");
            }

            return string.IsNullOrWhiteSpace(liveDisplayPhoneNumber)
                ? (false, "Display phone number is missing.")
                : (true, "Display phone number is present.");
        });

        await AddCheckAsync(checks, "Webhook Subscription", async () =>
        {
            if (!businessFound || string.IsNullOrWhiteSpace(decryptedAccessToken) || string.IsNullOrWhiteSpace(businessAccountId))
            {
                return (false, "Skipped: profile or access token is unavailable.");
            }

            var subscribed = await _metaGraphApiService.IsWebhookSubscribedAsync(businessAccountId, decryptedAccessToken, cancellationToken);
            webhookVerified = subscribed;
            return subscribed
                ? (true, "Webhook subscription is active.")
                : (false, "Webhook subscription is not active.");
        });

        await AddCheckAsync(checks, "Stored IDs Match", () =>
        {
            if (settings is null)
            {
                return (false, "No stored settings found.");
            }

            if (!businessFound || !phoneFound)
            {
                return (false, "Skipped: live Meta identifiers unavailable.");
            }

            var businessMatch = string.Equals(settings.BusinessAccountId, businessAccountId, StringComparison.Ordinal);
            var phoneMatch = string.Equals(settings.PhoneNumberId, phoneNumberId, StringComparison.Ordinal);
            var displayMatch = string.Equals(settings.DisplayPhoneNumber, phoneNumber, StringComparison.Ordinal);

            var allMatch = businessMatch && phoneMatch && displayMatch;
            return allMatch
                ? (true, "Stored IDs match Meta data.")
                : (false, "Stored IDs do not match current Meta data.");
        });

        var isHealthy = checks.All(c => c.Success);
        return new WhatsAppConnectionDiagnosticsDto(
            IsHealthy: isHealthy,
            ConnectionStatus: connectionStatus,
            GraphApiVersion: graphApiVersion,
            BusinessName: businessName,
            BusinessAccountId: businessAccountId,
            PhoneNumber: phoneNumber,
            PhoneNumberId: phoneNumberId,
            WebhookVerified: webhookVerified,
            AccessTokenValid: accessTokenValid,
            BusinessFound: businessFound,
            PhoneFound: phoneFound,
            ConfigurationValid: configurationValid,
            LastWebhookReceivedAt: lastWebhookReceivedAt,
            LastError: lastError,
            Checks: checks
        );
    }

    public async Task<WhatsAppConnectInitResponse> InitializeConnectAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        if (tenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Tenant context was not resolved.");
        }

        ValidateRequiredMetaConfiguration();

        var settings = await GetOrCreateSettingsAsync(tenantId, cancellationToken);
        settings.GraphApiVersion = _metaOptions.GraphApiVersion;
        settings.LastError = null;
        settings.LastErrorAt = null;
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await LogAuditAsync(tenantId, _tenantContext.UserId, "whatsapp_connect_init", true, null, cancellationToken);

        return new WhatsAppConnectInitResponse(
            AppId: _metaOptions.AppId,
            EmbeddedSignupConfigId: _metaOptions.EmbeddedSignupConfigId,
            GraphApiVersion: _metaOptions.GraphApiVersion
        );
    }

    public async Task CompleteEmbeddedSignupAsync(WhatsAppEmbeddedSignupCompleteRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var userId = _tenantContext.UserId;

        if (tenantId == Guid.Empty || !userId.HasValue || userId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Tenant or user context was not resolved.");
        }

        ValidateRequiredMetaConfiguration();

        if (string.IsNullOrWhiteSpace(request.AuthorizationCode) ||
            string.IsNullOrWhiteSpace(request.BusinessId) ||
            string.IsNullOrWhiteSpace(request.WabaId) ||
            string.IsNullOrWhiteSpace(request.PhoneNumberId))
        {
            throw new ArgumentException("Embedded signup payload is incomplete.");
        }

        var settings = await GetOrCreateSettingsAsync(tenantId, cancellationToken);
        settings.ConnectionStatus = WhatsAppConnectionStatus.Connecting;
        settings.IsConnected = false;
        settings.LastError = null;
        settings.LastErrorAt = null;
        settings.GraphApiVersion = _metaOptions.GraphApiVersion;
        settings.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var tokenResult = await _metaOAuthService.ExchangeAuthorizationCodeAsync(request.AuthorizationCode, cancellationToken);
            var waba = await _metaGraphApiService.GetWabaAsync(request.WabaId, tokenResult.AccessToken, cancellationToken);
            var phone = await _metaGraphApiService.GetPhoneNumberAsync(request.PhoneNumberId, tokenResult.AccessToken, cancellationToken);

            var webhookVerified = await _metaGraphApiService.VerifyWebhookAsync(request.WabaId, tokenResult.AccessToken, cancellationToken);

            settings.AccessToken = tokenResult.AccessToken;
            settings.BusinessId = request.BusinessId;
            settings.BusinessAccountId = request.WabaId;
            settings.PhoneNumberId = request.PhoneNumberId;
            settings.DisplayPhoneNumber = !string.IsNullOrWhiteSpace(phone.DisplayPhoneNumber)
                ? phone.DisplayPhoneNumber
                : request.DisplayPhoneNumber;
            settings.BusinessName = !string.IsNullOrWhiteSpace(waba.Name) ? waba.Name : settings.BusinessName;
            settings.WebhookUrl = _metaOptions.WebhookUrl;
            settings.VerifyToken = _metaOptions.VerifyToken;
            settings.WebhookVerified = webhookVerified;
            settings.WebhookVerifiedAt = webhookVerified ? DateTime.UtcNow : null;
            settings.LastWebhookReceivedAt = null;

            settings.TokenExpiresAt = tokenResult.ExpiresInSeconds.HasValue
                ? DateTime.UtcNow.AddSeconds(tokenResult.ExpiresInSeconds.Value)
                : null;

            if (webhookVerified)
            {
                settings.ConnectionStatus = WhatsAppConnectionStatus.Connected;
                settings.IsConnected = true;
                settings.ConnectedSince ??= DateTime.UtcNow;
                settings.LastError = null;
                settings.LastErrorAt = null;
            }
            else
            {
                settings.ConnectionStatus = WhatsAppConnectionStatus.Error;
                settings.IsConnected = false;
                settings.LastError = "Webhook verification failed.";
                settings.LastErrorAt = DateTime.UtcNow;
            }

            settings.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await LogAuditAsync(tenantId, userId, "whatsapp_embedded_signup_complete", webhookVerified, webhookVerified ? null : settings.LastError, cancellationToken);

            if (!webhookVerified)
            {
                throw new InvalidOperationException("Webhook verification failed.");
            }
        }
        catch (Exception ex)
        {
            var message = NormalizeError(ex is MetaApiException metaEx ? metaEx.AuditDetail : ex.Message);
            await SetConnectionErrorAsync(settings, message, cancellationToken);
            await LogAuditAsync(tenantId, userId, "whatsapp_embedded_signup_complete", false, message, cancellationToken);
            throw;
        }
    }

    public async Task<WhatsAppCallbackResultDto> HandleCallbackAsync(
        string? code,
        string? state,
        string? error,
        string? errorDescription,
        string? callbackHost,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Deprecated WhatsApp callback flow invoked. callbackHost={CallbackHost}, hasCode={HasCode}, hasState={HasState}, error={Error}", callbackHost, !string.IsNullOrWhiteSpace(code), !string.IsNullOrWhiteSpace(state), error);
        var reason = string.IsNullOrWhiteSpace(errorDescription) ? "Callback flow is no longer used. Use embedded signup complete endpoint." : errorDescription;
        await Task.CompletedTask;
        return BuildCallbackFailure("Deprecated callback endpoint.", reason, _tenantContext.TenantId, _tenantContext.UserId);
    }

    private static WhatsAppCapabilities BuildCapabilities(WhatsAppSettings? settings)
    {
        var connected = settings?.ConnectionStatus == WhatsAppConnectionStatus.Connected;
        var webhookReady = connected && (settings?.WebhookVerified ?? false);

        return new WhatsAppCapabilities
        {
            CanSendText = false,
            CanSendTemplate = false,
            CanSendDocuments = false,
            CanReceiveMessages = webhookReady,
            CanReceiveStatus = webhookReady,
            CanManageTemplates = false,
        };
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Tenant context was not resolved.");
        }

        var settings = await _context.WhatsAppSettings
            .Where(s => s.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            await LogAuditAsync(tenantId, _tenantContext.UserId, "whatsapp_disconnect", true, null, cancellationToken);
            return;
        }

        settings.AccessToken = null;
        settings.BusinessName = null;
        settings.BusinessId = null;
        settings.BusinessAccountId = null;
        settings.PhoneNumberId = null;
        settings.DisplayPhoneNumber = null;
        settings.VerifyToken = null;
        settings.WebhookSecret = null;
        settings.WebhookVerified = false;
        settings.WebhookVerifiedAt = null;
        settings.LastWebhookReceivedAt = null;
        settings.GraphApiVersion = _metaOptions.GraphApiVersion;
        settings.TokenExpiresAt = null;
        settings.LastSyncAt = null;
        settings.IsConnected = false;
        settings.ConnectedSince = null;
        settings.LastActivity = null;
        settings.ConnectionStatus = WhatsAppConnectionStatus.Disconnected;
        settings.LastError = null;
        settings.LastErrorAt = null;
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await LogAuditAsync(tenantId, _tenantContext.UserId, "whatsapp_disconnect", true, null, cancellationToken);
    }

    public async Task<IReadOnlyList<WhatsAppTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.WhatsAppTemplates
            .AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WhatsAppMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.WhatsAppMessages
            .AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    private WhatsAppConnectionStatus ResolveEffectiveConnectionStatus(WhatsAppSettings? settings)
    {
        if (settings is null)
        {
            return WhatsAppConnectionStatus.Disconnected;
        }

        if (settings.TokenExpiresAt.HasValue &&
            settings.TokenExpiresAt.Value <= DateTime.UtcNow &&
            settings.ConnectionStatus == WhatsAppConnectionStatus.Connected)
        {
            return WhatsAppConnectionStatus.TokenExpired;
        }

        return settings.ConnectionStatus;
    }

    private async Task FinalizeStaleConnectingStateAsync(WhatsAppSettings settings, CancellationToken cancellationToken)
    {
        if (settings.ConnectionStatus != WhatsAppConnectionStatus.Connecting)
        {
            return;
        }

        var maxConnecting = TimeSpan.FromMinutes(_metaOptions.MaxConnectingMinutes <= 0 ? 20 : _metaOptions.MaxConnectingMinutes);
        if (DateTime.UtcNow - settings.UpdatedAt < maxConnecting)
        {
            return;
        }

        settings.ConnectionStatus = WhatsAppConnectionStatus.Error;
        settings.IsConnected = false;
        settings.LastError = "Connection attempt timed out before callback completed.";
        settings.LastErrorAt = DateTime.UtcNow;
        settings.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string MapStatus(WhatsAppConnectionStatus status)
    {
        return status switch
        {
            WhatsAppConnectionStatus.Connecting => "Connecting",
            WhatsAppConnectionStatus.Connected => "Connected",
            WhatsAppConnectionStatus.Error => "Error",
            WhatsAppConnectionStatus.TokenExpired => "TokenExpired",
            _ => "Disconnected",
        };
    }

    private async Task<WhatsAppSettings> GetOrCreateSettingsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await _context.WhatsAppSettings
            .Where(s => s.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        settings = new WhatsAppSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConnectionStatus = WhatsAppConnectionStatus.Disconnected,
            GraphApiVersion = _metaOptions.GraphApiVersion,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _context.WhatsAppSettings.Add(settings);
        await _context.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private async Task<bool> ConsumeOAuthStateAsync(Guid tenantId, Guid userId, string nonce, CancellationToken cancellationToken)
    {
        var nonceHash = ComputeHash(nonce);
        var now = DateTime.UtcNow;

        var state = await _context.WhatsAppOAuthStates
            .Where(s => s.TenantId == tenantId && s.UserId == userId && s.NonceHash == nonceHash)
            .FirstOrDefaultAsync(cancellationToken);

        if (state is null || state.UsedAt.HasValue || state.ExpiresAt <= now)
        {
            return false;
        }

        state.UsedAt = now;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task PurgeExpiredOAuthStatesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var staleStates = await _context.WhatsAppOAuthStates
            .Where(s => s.TenantId == tenantId && (s.ExpiresAt <= now || s.UsedAt.HasValue))
            .ToListAsync(cancellationToken);

        if (staleStates.Count == 0)
        {
            return;
        }

        _context.WhatsAppOAuthStates.RemoveRange(staleStates);
    }

    private async Task<string?> ReadEncryptedAccessTokenAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"AccessToken\" FROM \"WhatsAppSettings\" WHERE \"TenantId\" = @tenantId LIMIT 1";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tenantId";
            parameter.Value = tenantId;
            command.Parameters.Add(parameter);

            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value as string;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task AddCheckAsync(
        IList<WhatsAppDiagnosticsCheckDto> checks,
        string name,
        Func<Task<(bool Success, string Message)>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action();
            stopwatch.Stop();
            checks.Add(new WhatsAppDiagnosticsCheckDto(name, result.Success, result.Message, stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            checks.Add(new WhatsAppDiagnosticsCheckDto(name, false, NormalizeError(ex.Message), stopwatch.ElapsedMilliseconds));
        }
    }

    private static Task AddCheckAsync(
        IList<WhatsAppDiagnosticsCheckDto> checks,
        string name,
        Func<(bool Success, string Message)> action)
    {
        return AddCheckAsync(checks, name, () => Task.FromResult(action()));
    }

    private async Task SetConnectionErrorAsync(WhatsAppSettings settings, string errorMessage, CancellationToken cancellationToken)
    {
        settings.ConnectionStatus = WhatsAppConnectionStatus.Error;
        settings.IsConnected = false;
        settings.LastError = NormalizeError(errorMessage);
        settings.LastErrorAt = DateTime.UtcNow;
        settings.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task LogAuditAsync(Guid tenantId, Guid? userId, string actionType, bool success, string? error, CancellationToken cancellationToken)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            EntityName = "WhatsApp",
            ActionType = actionType,
            EntityId = tenantId.ToString(),
            PerformedBy = userId.HasValue && userId.Value != Guid.Empty ? $"User:{userId}" : "System",
            NewValues = success ? "Success" : "Failure",
            OldValues = success ? null : NormalizeError(error),
            CreatedAt = DateTime.UtcNow,
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private WhatsAppCallbackResultDto BuildCallbackFailure(string message, string? detailedError, Guid? tenantId, Guid? userId)
    {
        var friendlyMessage = ToUserFriendlyMessage(message, detailedError);
        _logger.LogWarning("WhatsApp callback failure. TenantId: {TenantId}, UserId: {UserId}, Error: {Error}", tenantId, userId, detailedError);
        return new WhatsAppCallbackResultDto(
            Success: false,
            Message: friendlyMessage,
            RedirectUrl: BuildUiRedirectUrl("error", friendlyMessage)
        );
    }

    private string BuildUiRedirectUrl(string status, string? error)
    {
        var baseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var target = "/admin/settings/whatsapp";
        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
            (baseUri.Scheme == Uri.UriSchemeHttps || baseUri.Scheme == Uri.UriSchemeHttp))
        {
            target = $"{baseUri.GetLeftPart(UriPartial.Authority)}/admin/settings/whatsapp";
        }

        var query = new List<string> { $"wa_status={Uri.EscapeDataString(status)}" };
        if (!string.IsNullOrWhiteSpace(error))
        {
            query.Add($"wa_error={Uri.EscapeDataString(error)}");
        }

        return $"{target}?{string.Join("&", query)}";
    }

    private static string NormalizeError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown error";
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 900)
        {
            return trimmed;
        }

        return trimmed[..900];
    }

    private void ValidateRequiredMetaConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_metaOptions.AppId) ||
            string.IsNullOrWhiteSpace(_metaOptions.AppSecret) ||
            string.IsNullOrWhiteSpace(_metaOptions.RedirectUri) ||
            string.IsNullOrWhiteSpace(_metaOptions.WebhookUrl) ||
            string.IsNullOrWhiteSpace(_metaOptions.VerifyToken))
        {
            throw new InvalidOperationException("WhatsApp Meta configuration is incomplete.");
        }
    }

    private bool IsCallbackHostValid(string? callbackHost)
    {
        if (string.IsNullOrWhiteSpace(callbackHost))
        {
            return false;
        }

        if (!Uri.TryCreate(_metaOptions.RedirectUri, UriKind.Absolute, out var callbackUri))
        {
            return false;
        }

        return string.Equals(callbackHost, callbackUri.Authority, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static string ToUserFriendlyMessage(string fallback, string? detailedError)
    {
        if (string.IsNullOrWhiteSpace(detailedError))
        {
            return fallback;
        }

        var value = detailedError.ToLowerInvariant();
        if (value.Contains("access_denied") || value.Contains("cancel"))
        {
            return "The Meta login was cancelled by the user.";
        }

        if (value.Contains("authorization code") || value.Contains("oauth") || value.Contains("invalid code"))
        {
            return "The authorization code is invalid or expired. Please try connecting again.";
        }

        if (value.Contains("token exchange"))
        {
            return "Could not exchange authorization code for token.";
        }

        if (value.Contains("webhook"))
        {
            return "Webhook verification failed. Please verify your Meta webhook configuration.";
        }

        if (value.Contains("graph") || value.Contains("facebook"))
        {
            return "Meta Graph API is temporarily unavailable. Please try again.";
        }

        return fallback;
    }
}
