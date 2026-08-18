using System.Security.Claims;
using System.Text.Encodings.Web;
using BCrypt.Net;
using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clienta.Api.Authentication;

public class ImagingGatewayAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string HeaderName = "X-Imaging-Gateway-Key";
    private const string Prefix = "cl_img_";
    private readonly AppDbContext _db;
    private readonly ITenantAccessValidator _tenantAccessValidator;

    public ImagingGatewayAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext db,
        ITenantAccessValidator tenantAccessValidator)
        : base(options, logger, encoder)
    {
        _db = db;
        _tenantAccessValidator = tenantAccessValidator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            return AuthenticateResult.Fail($"Missing {HeaderName} header.");
        }

        var rawKey = headerValues.ToString().Trim();
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return AuthenticateResult.Fail($"Missing {HeaderName} header.");
        }

        if (!TryParseKey(rawKey, out var keyId, out var secret))
        {
            return AuthenticateResult.Fail("Invalid imaging gateway key format.");
        }

        var credential = await _db.ImagingGatewayCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.KeyId == keyId, Context.RequestAborted);

        if (credential == null || !credential.IsActive)
        {
            return AuthenticateResult.Fail("Invalid imaging gateway credential.");
        }

        if (!BCrypt.Net.BCrypt.Verify(secret, credential.KeyHash))
        {
            return AuthenticateResult.Fail("Invalid imaging gateway credential.");
        }

        var tenantAllowed = await _tenantAccessValidator.IsTenantAllowedAsync(credential.TenantId, Context.RequestAborted);
        if (!tenantAllowed)
        {
            return AuthenticateResult.Fail("Tenant is suspended or unavailable.");
        }

        var claims = new List<Claim>
        {
            new("tenant_id", credential.TenantId.ToString()),
            new("gateway_credential_id", credential.Id.ToString()),
            new("gateway_key_id", credential.KeyId)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }

    private static bool TryParseKey(string rawKey, out string keyId, out string secret)
    {
        keyId = string.Empty;
        secret = string.Empty;

        if (!rawKey.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = rawKey[Prefix.Length..];
        var separatorIndex = payload.IndexOf('_');
        if (separatorIndex <= 0 || separatorIndex >= payload.Length - 1)
        {
            return false;
        }

        keyId = payload[..separatorIndex];
        secret = payload[(separatorIndex + 1)..];

        return IsAllowedToken(keyId) && IsAllowedToken(secret);
    }

    private static bool IsAllowedToken(string value)
    {
        foreach (var ch in value)
        {
            if (!(char.IsDigit(ch) || (ch >= 'A' && ch <= 'Z')))
            {
                return false;
            }
        }

        return value.Length > 0;
    }
}
