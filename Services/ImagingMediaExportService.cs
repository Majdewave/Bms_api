using Amazon.S3;
using Amazon.S3.Model;
using Clienta.Api.Entities;
using FellowOakDicom;
using FellowOakDicom.Media;
using System.IO.Compression;

namespace Clienta.Api.Services;

public enum ImagingMediaExportFailure
{
    CapacityExceeded,
    IncompleteStudy,
    ViewerUnavailable,
    DicomDirectoryFailed,
    TemporaryStorageUnavailable,
}

public sealed class ImagingMediaExportException : Exception
{
    public ImagingMediaExportException(ImagingMediaExportFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public ImagingMediaExportFailure Failure { get; }
}

public interface IImagingMediaExportService
{
    Task<Stream> CreateArchiveAsync(IReadOnlyList<ImagingInstance> instances, CancellationToken cancellationToken);
}

public sealed class ImagingMediaExportService : IImagingMediaExportService
{
    private const int CopyBufferSize = 1024 * 1024;
    private const int MaxConcurrentExports = 1;
    private static readonly SemaphoreSlim ExportGate = new(MaxConcurrentExports, MaxConcurrentExports);

    private const string Launcher = """
        @echo off
        setlocal
        pushd "%~dp0" || exit /b 1
        set "WEASIS_EXE=VIEWER\Weasis-4.8.0\Weasis.exe"
        set "DICOM_DIR=DICOM"

        if not exist "%WEASIS_EXE%" (
          echo Weasis was not found in the package's VIEWER folder.
          pause
          exit /b 1
        )
        if not exist "%DICOM_DIR%" (
          echo The package's DICOM folder was not found.
          pause
          exit /b 1
        )

        start "" /max "%WEASIS_EXE%" "$dicom:get -l DICOM"
        popd
        endlocal
        """;

    private const string Readme = """
        CLIENTA DICOM EXAM

        Windows:
        1. Extract or copy the complete package.
        2. Double-click START_VIEWER.bat.
        3. The included DICOM viewer will open the examination.

        The DICOM files are also available in the DICOM folder for import into
        another compatible PACS/DICOM viewer.

        The bundled open-source Weasis distribution is provided for viewing
        DICOM media. See LICENSES for applicable open-source and third-party
        notices. Weasis is not represented as a certified diagnostic medical
        device; use it in accordance with applicable institutional and legal
        requirements.
        """;

    private readonly IAmazonS3 _s3Client;
    private readonly IWeasisPortablePackageProvider _packageProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImagingMediaExportService> _logger;

    public ImagingMediaExportService(
        IAmazonS3 s3Client,
        IWeasisPortablePackageProvider packageProvider,
        IConfiguration configuration,
        ILogger<ImagingMediaExportService> logger)
    {
        _s3Client = s3Client;
        _packageProvider = packageProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Stream> CreateArchiveAsync(
        IReadOnlyList<ImagingInstance> instances,
        CancellationToken cancellationToken)
    {
        if (!await ExportGate.WaitAsync(0, cancellationToken))
        {
            throw new ImagingMediaExportException(
                ImagingMediaExportFailure.CapacityExceeded,
                "Another media export is already being prepared.");
        }

        FileStream? archiveFile = null;
        string? stagingDirectory = null;
        var leaseTransferred = false;
        try
        {
            if (instances.Count == 0)
            {
                throw new ImagingMediaExportException(
                    ImagingMediaExportFailure.IncompleteStudy,
                    "The study has no DICOM instances.");
            }

            var tempRoot = _configuration["ImagingMediaExport:TemporaryDirectory"];
            if (string.IsNullOrWhiteSpace(tempRoot))
            {
                tempRoot = Path.GetTempPath();
            }

            Directory.CreateDirectory(tempRoot);
            stagingDirectory = Path.Combine(tempRoot, $"clienta-imaging-export-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDirectory);

            string packagePath;
            try
            {
                packagePath = await _packageProvider.GetPackagePathAsync(cancellationToken);
            }
            catch (WeasisPortablePackageUnavailableException ex)
            {
                throw new ImagingMediaExportException(
                    ImagingMediaExportFailure.ViewerUnavailable,
                    "The portable viewer is temporarily unavailable.",
                    ex);
            }

            var archivePath = Path.Combine(tempRoot, $"clienta-imaging-export-{Guid.NewGuid():N}.zip");
            archiveFile = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);

            using (var archive = new ZipArchive(archiveFile, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddViewerFilesAsync(packagePath, archive, cancellationToken);

                var dicomDirectory = new DicomDirectory(explicitVr: true)
                {
                    FileSetID = "CLIENTA",
                    SourceApplicationEntityTitle = "CLIENTA",
                };

                for (var index = 0; index < instances.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var instance = instances[index];
                    var fileId = (index + 1).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
                    var dicomPath = Path.Combine(stagingDirectory, fileId);

                    try
                    {
                        using var response = await _s3Client.GetObjectAsync(new GetObjectRequest
                        {
                            BucketName = instance.S3Bucket!,
                            Key = instance.S3Key!,
                        }, cancellationToken);

                        await using (var dicomOutput = new FileStream(
                            dicomPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            CopyBufferSize,
                            FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            await response.ResponseStream.CopyToAsync(dicomOutput, CopyBufferSize, cancellationToken);
                        }
                    }
                    catch (AmazonS3Exception ex)
                    {
                        throw new ImagingMediaExportException(
                            ImagingMediaExportFailure.IncompleteStudy,
                            "One or more DICOM instances are unavailable.",
                            ex);
                    }

                    try
                    {
                        var dicomFile = await DicomFile.OpenAsync(dicomPath, FileReadOption.SkipLargeTags);
                        dicomDirectory.AddFile(dicomFile, $"DICOM\\{fileId}");
                    }
                    catch (Exception ex) when (
                        ex is DicomException or InvalidDataException or ArgumentException or InvalidOperationException)
                    {
                        throw new ImagingMediaExportException(
                            ImagingMediaExportFailure.DicomDirectoryFailed,
                            "The DICOM directory could not be generated.",
                            ex);
                    }

                    var entry = archive.CreateEntry($"DICOM/{fileId}", CompressionLevel.NoCompression);
                    await using (var entryStream = entry.Open())
                    await using (var dicomInput = new FileStream(
                        dicomPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        CopyBufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await dicomInput.CopyToAsync(entryStream, CopyBufferSize, cancellationToken);
                    }

                    File.Delete(dicomPath);
                }

                var dicomDirectoryPath = Path.Combine(stagingDirectory, "DICOMDIR");
                try
                {
                    await dicomDirectory.SaveAsync(dicomDirectoryPath);
                }
                catch (Exception ex) when (
                    ex is DicomException or IOException or ArgumentException or InvalidOperationException)
                {
                    throw new ImagingMediaExportException(
                        ImagingMediaExportFailure.DicomDirectoryFailed,
                        "The DICOM directory could not be generated.",
                        ex);
                }

                var directoryEntry = archive.CreateEntry("DICOMDIR", CompressionLevel.NoCompression);
                await using var directoryEntryStream = directoryEntry.Open();
                await using var directoryInput = new FileStream(
                    dicomDirectoryPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    CopyBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await directoryInput.CopyToAsync(directoryEntryStream, CopyBufferSize, cancellationToken);
            }

            await archiveFile.FlushAsync(cancellationToken);
            archiveFile.Position = 0;
            var responseStream = new ExportLeaseStream(archiveFile, ExportGate);
            archiveFile = null;
            leaseTransferred = true;
            return responseStream;
        }
        catch (ImagingMediaExportException)
        {
            throw;
        }
        catch (AmazonS3Exception ex)
        {
            throw new ImagingMediaExportException(
                ImagingMediaExportFailure.IncompleteStudy,
                "One or more DICOM instances are unavailable.",
                ex);
        }
        catch (IOException ex)
        {
            throw new ImagingMediaExportException(
                ImagingMediaExportFailure.TemporaryStorageUnavailable,
                "The media export could not be prepared in temporary storage.",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ImagingMediaExportException(
                ImagingMediaExportFailure.TemporaryStorageUnavailable,
                "The media export could not be prepared in temporary storage.",
                ex);
        }
        finally
        {
            if (archiveFile != null)
            {
                await archiveFile.DisposeAsync();
            }

            if (!string.IsNullOrWhiteSpace(stagingDirectory))
            {
                TryDeleteStagingDirectory(stagingDirectory);
            }

            if (!leaseTransferred)
            {
                ExportGate.Release();
            }
        }
    }

    public static string CreateDownloadFileName(string? accessionNumber, Guid studyId)
    {
        var safeAccession = string.Concat((accessionNumber ?? string.Empty).Where(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'));
        if (string.IsNullOrWhiteSpace(safeAccession))
        {
            safeAccession = studyId.ToString("N");
        }

        return $"CLIENTA-{safeAccession}-CD-USB.zip";
    }

    private async Task AddViewerFilesAsync(
        string packagePath,
        ZipArchive outputArchive,
        CancellationToken cancellationToken)
    {
        using var viewerArchive = ZipFile.OpenRead(packagePath);
        var files = viewerArchive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
        var normalizedNames = files.Select(entry => NormalizeAssetEntryPath(entry.FullName)).ToList();
        if (!normalizedNames.Contains("VIEWER/Weasis-4.8.0/Weasis.exe", StringComparer.OrdinalIgnoreCase) ||
            !normalizedNames.Any(path => path.StartsWith("LICENSES/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ImagingMediaExportException(
                ImagingMediaExportFailure.ViewerUnavailable,
                "The pinned viewer package is missing required viewer or license files.");
        }

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceEntry = files[index];
            var entryPath = normalizedNames[index];
            if (!entryPath.StartsWith("VIEWER/", StringComparison.OrdinalIgnoreCase) &&
                !entryPath.StartsWith("LICENSES/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ImagingMediaExportException(
                    ImagingMediaExportFailure.ViewerUnavailable,
                    "The pinned viewer package contains an unexpected file.");
            }

            var destinationEntry = outputArchive.CreateEntry(entryPath, CompressionLevel.Optimal);
            await using var sourceStream = sourceEntry.Open();
            await using var destinationStream = destinationEntry.Open();
            await sourceStream.CopyToAsync(destinationStream, CopyBufferSize, cancellationToken);
        }

        await AddTextEntryAsync(outputArchive, "START_VIEWER.bat", Launcher, cancellationToken);
        await AddTextEntryAsync(outputArchive, "README.txt", Readme, cancellationToken);
    }

    private static string NormalizeAssetEntryPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new ImagingMediaExportException(
                ImagingMediaExportFailure.ViewerUnavailable,
                "The pinned viewer package contains an unsafe path.");
        }

        return normalized;
    }

    private static async Task AddTextEntryAsync(
        ZipArchive archive,
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private void TryDeleteStagingDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Could not clean temporary imaging media export data.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Could not clean temporary imaging media export data.");
        }
    }

    private sealed class ExportLeaseStream : Stream
    {
        private readonly Stream _inner;
        private readonly SemaphoreSlim _gate;
        private int _disposed;

        public ExportLeaseStream(Stream inner, SemaphoreSlim gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _inner.Dispose();
                _gate.Release();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await _inner.DisposeAsync();
                _gate.Release();
            }

            GC.SuppressFinalize(this);
        }
    }
}
