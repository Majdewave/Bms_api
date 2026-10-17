using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;

namespace Clienta.Api.Services;

public sealed record PaymentConfirmationProofUpload(
    string StorageKey,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256);

public sealed class PaymentConfirmationProofReadResult : IDisposable
{
    private readonly IDisposable? _owner;

    public PaymentConfirmationProofReadResult(Stream stream, string fileName, string contentType, IDisposable? owner = null)
    {
        Stream = stream;
        FileName = fileName;
        ContentType = contentType;
        _owner = owner;
    }

    public Stream Stream { get; }
    public string FileName { get; }
    public string ContentType { get; }

    public void Dispose()
    {
        Stream.Dispose();
        _owner?.Dispose();
    }
}

public interface IPaymentConfirmationProofStorage
{
    Task<PaymentConfirmationProofUpload> UploadAsync(Guid tenantId, Guid requestId, IFormFile file, CancellationToken cancellationToken = default);
    Task<PaymentConfirmationProofReadResult?> OpenReadAsync(string storageKey, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed class PaymentConfirmationProofStorage : IPaymentConfirmationProofStorage
{
    private const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public PaymentConfirmationProofStorage(IAmazonS3 s3, IConfiguration configuration)
    {
        _s3 = s3;
        _bucket = configuration["AWS:BucketName"] ?? throw new InvalidOperationException("AWS bucket is not configured.");
    }

    public async Task<PaymentConfirmationProofUpload> UploadAsync(Guid tenantId, Guid requestId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0 || file.Length > MaxFileSize)
            throw new InvalidDataException("Proof file size is invalid.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedTypes.TryGetValue(extension, out var contentType) || !string.Equals(file.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Proof file type is not allowed.");

        await using var input = file.OpenReadStream();
        var bytes = new byte[16];
        var read = await input.ReadAsync(bytes.AsMemory(), cancellationToken);
        if (!HasValidSignature(extension, bytes, read))
            throw new InvalidDataException("Proof file signature is invalid.");

        input.Position = 0;
        var hash = await SHA256.HashDataAsync(input, cancellationToken);
        input.Position = 0;
        var storageKey = $"tenants/{tenantId}/payment-confirmations/{requestId}/{Guid.NewGuid():N}{extension}";
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = storageKey,
            InputStream = input,
            ContentType = contentType,
            CannedACL = S3CannedACL.Private,
        }, cancellationToken);

        return new PaymentConfirmationProofUpload(storageKey, Path.GetFileName(file.FileName), contentType, file.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
        _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = storageKey }, cancellationToken);

    public async Task<PaymentConfirmationProofReadResult?> OpenReadAsync(string storageKey, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, storageKey, cancellationToken);
            return new PaymentConfirmationProofReadResult(response.ResponseStream, Path.GetFileName(fileName), contentType, response);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static bool HasValidSignature(string extension, byte[] bytes, int length) => extension switch
    {
        ".pdf" => length >= 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46 && bytes[4] == 0x2D,
        ".jpg" or ".jpeg" => length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        ".png" => length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        _ => false,
    };
}