using System.Security.Cryptography;
using System.Text;

namespace Clienta.Api.Services;

public class ImagingAccessionNumberGenerator : IImagingAccessionNumberGenerator
{
    private const string Base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int RandomLength = 6;

    public string Generate(string modality, DateTime utcDate)
    {
        if (string.IsNullOrWhiteSpace(modality))
        {
            throw new ArgumentException("Modality is required.", nameof(modality));
        }

        var normalizedModality = modality.Trim().ToUpperInvariant();
        var randomPart = GenerateRandomBase36(RandomLength);
        return $"{normalizedModality}{utcDate:yyMMdd}{randomPart}";
    }

    private static string GenerateRandomBase36(int length)
    {
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(Base36[RandomNumberGenerator.GetInt32(Base36.Length)]);
        }

        return builder.ToString();
    }
}
