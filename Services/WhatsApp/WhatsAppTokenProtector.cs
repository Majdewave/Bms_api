using System.Security.Cryptography;
using System.Text;

namespace Clienta.Api.Services.WhatsApp;

public static class WhatsAppTokenProtector
{
    private const string FallbackKeyMaterial = "clienta-whatsapp-stage1-key";

    public static string? Encrypt(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return plainText;
        }

        var key = GetKey();

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var payload = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, payload, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(payload);
    }

    public static string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return cipherText;
        }

        try
        {
            var payload = Convert.FromBase64String(cipherText);
            var key = GetKey();

            using var aes = Aes.Create();
            aes.Key = key;

            var ivSize = aes.BlockSize / 8;
            if (payload.Length <= ivSize)
            {
                return null;
            }

            var iv = new byte[ivSize];
            var cipherBytes = new byte[payload.Length - ivSize];
            Buffer.BlockCopy(payload, 0, iv, 0, ivSize);
            Buffer.BlockCopy(payload, ivSize, cipherBytes, 0, cipherBytes.Length);

            using var decryptor = aes.CreateDecryptor(aes.Key, iv);
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] GetKey()
    {
        var keySource = Environment.GetEnvironmentVariable("CLIENTA_WHATSAPP_TOKEN_KEY");
        if (string.IsNullOrWhiteSpace(keySource))
        {
            keySource = FallbackKeyMaterial;
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(keySource));
    }
}
