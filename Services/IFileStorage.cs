using System.IO;
using System.Threading.Tasks;

public interface IFileStorage
{
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType);
    Task DeleteAsync(string fileKey);
}
