using System.Security.Cryptography;
using System.Text;

namespace Clienta.Api.Services.Integrations;

public interface IIntegrationSecretProtector
{
    bool IsAvailable { get; }

    // Returns null when no key is configured (fail closed).
    string? Protect(string plainText, string purpose);

    string? Unprotect(string? protectedValue, string purpose);
}

// AES-GCM; key is SHA-256 of an environment secret, same key-sourcing approach as WhatsAppTokenProtector but with no
// built-in fallback key and with the tenant/provider bound as associated data so ciphertext cannot be moved between rows.
public sealed class IntegrationSecretProtector : IIntegrationSecretProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[]? _key;

    public IntegrationSecretProtector(IConfiguration configuration)
    {
        var source = Environment.GetEnvironmentVariable("CLIENTA_INTEGRATION_SECRET_KEY");
        if (string.IsNullOrWhiteSpace(source))
            source = Environment.GetEnvironmentVariable("CLIENTA_WHATSAPP_TOKEN_KEY");
        if (string.IsNullOrWhiteSpace(source))
            source = configuration["Integrations:SecretKey"];

        _key = string.IsNullOrWhiteSpace(source) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(source));
    }

    public IntegrationSecretProtector(string keySource)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keySource));
    }

    public bool IsAvailable => _key is not null;

    public string? Protect(string plainText, string purpose)
    {
        if (_key is null) return null;

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(purpose));

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string? Unprotect(string? protectedValue, string purpose)
    {
        if (_key is null || string.IsNullOrWhiteSpace(protectedValue) || !protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        try
        {
            var payload = Convert.FromBase64String(protectedValue[Prefix.Length..]);
            if (payload.Length <= NonceSize + TagSize) return null;

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var cipher = payload.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(purpose));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
