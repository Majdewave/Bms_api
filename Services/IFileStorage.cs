using System.IO;
using System.Threading;
using System.Threading.Tasks;

public interface IFileStorage
{
    Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType);

    Task<Stream> OpenReadAsync(
        string fileKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string fileKey);
}