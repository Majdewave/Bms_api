using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

public class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public S3FileStorage(IAmazonS3 s3, IConfiguration config)
    {
        _s3 = s3;
        _bucket = config["AWS:BucketName"];
    }

    public async Task<string> UploadAsync(Stream fileStream, string key, string contentType)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = fileStream,
            ContentType = contentType
        };

        await _s3.PutObjectAsync(request);

        return $"https://{_bucket}.s3.amazonaws.com/{key}";
    }

    public async Task DeleteAsync(string key)
    {
        await _s3.DeleteObjectAsync(_bucket, key);
    }
}
