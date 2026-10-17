using System.Security.Cryptography;
using System.Text;

namespace Clienta.Api.Services;

public sealed record PaymentConfirmationToken(string RawToken, string TokenHash);

public interface IPaymentConfirmationTokenService
{
    PaymentConfirmationToken Create();
    string Hash(string rawToken);
}

public sealed class PaymentConfirmationTokenService : IPaymentConfirmationTokenService
{
    public PaymentConfirmationToken Create()
    {
        var rawToken = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return new PaymentConfirmationToken(rawToken, Hash(rawToken));
    }

    public string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}