using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Clienta.Api.Services;

public sealed class LocalPaymentConfirmationProofStorage : IPaymentConfirmationProofStorage
{
    private const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    private readonly string _rootDirectory;

    public LocalPaymentConfirmationProofStorage(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new InvalidOperationException("An isolated Development proof directory must be configured.");
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task<PaymentConfirmationProofUpload> UploadAsync(Guid tenantId, Guid requestId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0 || file.Length > MaxFileSize)
            throw new InvalidDataException("Proof file size is invalid.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedTypes.TryGetValue(extension, out var contentType)
            || !string.Equals(file.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Proof file type is not allowed.");

        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await input.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (bytes.LongLength > MaxFileSize || !HasValidSignature(extension, bytes))
            throw new InvalidDataException("Proof file signature or size is invalid.");

        var storageKey = $"tenants/{tenantId}/payment-confirmations/{requestId}/{Guid.NewGuid():N}{extension}";
        var destination = ResolveStoragePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await output.WriteAsync(bytes, cancellationToken);
        }

        return new PaymentConfirmationProofUpload(storageKey, Path.GetFileName(file.FileName), contentType, bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public Task<PaymentConfirmationProofReadResult?> OpenReadAsync(string storageKey, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStoragePath(storageKey);
        if (!File.Exists(path)) return Task.FromResult<PaymentConfirmationProofReadResult?>(null);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<PaymentConfirmationProofReadResult?>(new PaymentConfirmationProofReadResult(stream, Path.GetFileName(fileName), contentType));
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStoragePath(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolveStoragePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey))
            throw new InvalidDataException("Invalid proof storage key.");
        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid proof storage key.");
        return fullPath;
    }

    private static bool HasValidSignature(string extension, byte[] bytes) => extension switch
    {
        ".pdf" => bytes.Length >= 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46 && bytes[4] == 0x2D,
        ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        ".png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        _ => false,
    };
}
