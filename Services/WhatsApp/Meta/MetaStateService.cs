using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Clienta.Api.Services.WhatsApp.Meta;

public class MetaStateService : IMetaStateService
{
    private static readonly TimeSpan MaxStateAge = TimeSpan.FromMinutes(15);
    private readonly IDataProtector _protector;

    public MetaStateService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("Clienta.WhatsApp.Meta.State.v1");
    }

    public string Create(Guid tenantId, Guid userId, string nonce)
    {
        var payload = new MetaStatePayload(tenantId, userId, nonce, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(payload);
        return _protector.Protect(json);
    }

    public bool TryValidate(string? protectedState, out MetaStatePayload payload, out string error)
    {
        payload = default!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(protectedState))
        {
            error = "Missing callback state.";
            return false;
        }

        try
        {
            var json = _protector.Unprotect(protectedState);
            var parsed = JsonSerializer.Deserialize<MetaStatePayload>(json);
            if (parsed is null)
            {
                error = "Invalid callback state payload.";
                return false;
            }

            if (DateTime.UtcNow - parsed.IssuedAtUtc > MaxStateAge)
            {
                error = "Callback state has expired.";
                return false;
            }

            if (parsed.TenantId == Guid.Empty || parsed.UserId == Guid.Empty || string.IsNullOrWhiteSpace(parsed.Nonce))
            {
                error = "Invalid callback state scope.";
                return false;
            }

            payload = parsed;
            return true;
        }
        catch
        {
            error = "Failed to validate callback state.";
            return false;
        }
    }
}
