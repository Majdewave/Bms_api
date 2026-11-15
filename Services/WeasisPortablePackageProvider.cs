using Amazon.S3;
using Amazon.S3.Model;
using System.Buffers;
using System.Security.Cryptography;

namespace Clienta.Api.Services;

public interface IWeasisPortablePackageProvider
{
    Task<string> GetPackagePathAsync(CancellationToken cancellationToken);
}

public sealed class WeasisPortablePackageUnavailableException : Exception
{
    public WeasisPortablePackageUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class WeasisPortablePackageProvider : IWeasisPortablePackageProvider
{
    public const string ExpectedSha256 = "532DA2D1D799100B3B5AA6C2E6EA2CD5D08F2A6E1FFDE5D86120E94C8C44D7CE";
    public const string DefaultObjectKey = "weasis/Weasis-4.8.0.zip";

    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WeasisPortablePackageProvider> _logger;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private string? _cachedPackagePath;

    public WeasisPortablePackageProvider(
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<WeasisPortablePackageProvider> logger)
    {
        _s3Client = s3Client;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GetPackagePathAsync(CancellationToken cancellationToken)
    {
        var bucket = _configuration["AWS:WeasisAssetsBucketName"]?.Trim();
        var key = _configuration["AWS:WeasisPortablePackageKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = DefaultObjectKey;
        }

        if (string.IsNullOrWhiteSpace(bucket) || !IsSafeObjectKey(key))
        {
            throw new WeasisPortablePackageUnavailableException("The pinned Weasis package is not configured.");
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedPackagePath) && File.Exists(_cachedPackagePath))
            {
                return _cachedPackagePath;
            }

            var cachePath = Path.Combine(
                Path.GetTempPath(),
                $"clienta-weasis-4.8.0-{ExpectedSha256}.zip");

            try
            {
                if (File.Exists(cachePath))
                {
                    if (await HasExpectedHashAsync(cachePath, cancellationToken))
                    {
                        _cachedPackagePath = cachePath;
                        return cachePath;
                    }

                    File.Delete(cachePath);
                }

                var temporaryPath = Path.Combine(Path.GetTempPath(), $"clienta-weasis-{Guid.NewGuid():N}.tmp");
                try
                {
                    using var response = await _s3Client.GetObjectAsync(new GetObjectRequest
                    {
                        BucketName = bucket,
                        Key = key,
                    }, cancellationToken);

                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    await using (var output = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
                        try
                        {
                            int bytesRead;
                            while ((bytesRead = await response.ResponseStream.ReadAsync(
                                buffer.AsMemory(0, buffer.Length),
                                cancellationToken)) > 0)
                            {
                                hash.AppendData(buffer, 0, bytesRead);
                                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }

                    var actualHash = Convert.ToHexString(hash.GetHashAndReset());
                    if (!string.Equals(actualHash, ExpectedSha256, StringComparison.Ordinal))
                    {
                        throw new WeasisPortablePackageUnavailableException("The pinned Weasis package failed integrity validation.");
                    }

                    File.Move(temporaryPath, cachePath, overwrite: true);
                    _cachedPackagePath = cachePath;
                    _logger.LogInformation("Pinned Weasis 4.8.0 package cached and integrity-verified.");
                    return cachePath;
                }
                finally
                {
                    TryDelete(temporaryPath);
                }
            }
            catch (WeasisPortablePackageUnavailableException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AmazonS3Exception ex)
            {
                throw new WeasisPortablePackageUnavailableException("The pinned Weasis package could not be retrieved.", ex);
            }
            catch (IOException ex)
            {
                throw new WeasisPortablePackageUnavailableException("The pinned Weasis package could not be cached.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new WeasisPortablePackageUnavailableException("The pinned Weasis package could not be cached.", ex);
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static bool IsSafeObjectKey(string key) =>
        !key.StartsWith("/", StringComparison.Ordinal) &&
        !key.Contains('\\') &&
        !key.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or "..");

    private static async Task<bool> HasExpectedHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), ExpectedSha256, StringComparison.Ordinal);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Could not remove a temporary Weasis package file.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Could not remove a temporary Weasis package file.");
        }
    }
}
