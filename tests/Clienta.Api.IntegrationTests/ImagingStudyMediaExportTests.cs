using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using FellowOakDicom;
using FellowOakDicom.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class ImagingStudyMediaExportTests
{
    [TestMethod]
    public async Task ExportStudyMedia_IncludesAllSeriesInstancesViewerLicensesLauncherAndDicomdir()
    {
        await using var fixture = await ExportFixture.CreateAsync();
        using var s3 = new FakeS3Client();
        var sourceBytes = new Dictionary<string, byte[]>();
        for (var index = 1; index <= fixture.Instances.Count; index++)
        {
            var bytes = CreateDicomBytes(index, index == 3 ? 2 : 1);
            sourceBytes[$"key-{index}"] = bytes;
            s3.Objects[$"key-{index}"] = bytes;
        }

        var packagePath = fixture.CreateViewerPackage();
        var service = fixture.CreateExportService(s3, new TestPackageProvider(packagePath));
        var controller = fixture.CreateController(s3, service);

        var result = await controller.ExportStudyMedia(fixture.Study.Id, CancellationToken.None);

        var file = result as FileStreamResult;
        Assert.IsNotNull(file);
        try
        {
            Assert.AreEqual("application/zip", file.ContentType);
            Assert.AreEqual("CLIENTA-A-1-CD-USB.zip", file.FileDownloadName);
            Assert.AreEqual(fixture.Instances.Count, s3.Downloads.Count);
            using (var archive = new ZipArchive(file.FileStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var dicomEntries = archive.Entries
                    .Where(entry => entry.FullName.StartsWith("DICOM/", StringComparison.Ordinal))
                    .ToList();
                Assert.AreEqual(fixture.Instances.Count, dicomEntries.Count);
                Assert.IsNotNull(archive.GetEntry("VIEWER/Weasis-4.8.0/Weasis.exe"));
                Assert.IsNotNull(archive.GetEntry("LICENSES/EPL-2.0.txt"));
                Assert.IsNotNull(archive.GetEntry("START_VIEWER.bat"));
                Assert.IsNotNull(archive.GetEntry("README.txt"));
                Assert.IsNotNull(archive.GetEntry("DICOMDIR"));

                foreach (var entry in dicomEntries)
                {
                    var key = $"key-{int.Parse(Path.GetFileName(entry.FullName), System.Globalization.CultureInfo.InvariantCulture)}";
                    using var input = entry.Open();
                    using var output = new MemoryStream();
                    await input.CopyToAsync(output);
                    CollectionAssert.AreEqual(sourceBytes[key], output.ToArray(), "Exported DICOM bytes must match S3 exactly.");
                }

                using var launcherReader = new StreamReader(archive.GetEntry("START_VIEWER.bat")!.Open());
                var launcher = await launcherReader.ReadToEndAsync();
                StringAssert.Contains(launcher, @"pushd ""%~dp0""");
                StringAssert.Contains(launcher, @"VIEWER\Weasis-4.8.0\Weasis.exe");
                StringAssert.Contains(launcher, "$dicom:get -l DICOM");
                Assert.IsFalse(Regex.IsMatch(launcher, @"[A-Z]:\\", RegexOptions.IgnoreCase));

                using var readmeReader = new StreamReader(archive.GetEntry("README.txt")!.Open());
                StringAssert.Contains(await readmeReader.ReadToEndAsync(), "LICENSES");

                using var directoryInput = archive.GetEntry("DICOMDIR")!.Open();
                using var directoryBytes = new MemoryStream();
                await directoryInput.CopyToAsync(directoryBytes);
                directoryBytes.Position = 0;
                var dicomDirectory = DicomDirectory.Open(directoryBytes);
                var records = dicomDirectory.Dataset.GetSequence(DicomTag.DirectoryRecordSequence).Items;
                var referencedFileIds = records
                    .Where(record => record.Contains(DicomTag.ReferencedFileID))
                    .Select(record => string.Join('/', record.GetValues<string>(DicomTag.ReferencedFileID)))
                    .ToHashSet(StringComparer.Ordinal);
                CollectionAssert.AreEquivalent(
                    dicomEntries.Select(entry => entry.FullName).ToArray(),
                    referencedFileIds.ToArray());
                Assert.AreEqual(2, records.Count(record =>
                    record.GetSingleValueOrDefault(DicomTag.DirectoryRecordType, string.Empty) == "SERIES"));
            }
        }
        finally
        {
            await file.FileStream.DisposeAsync();
        }

        Assert.AreEqual(0, GetFileCount(fixture.TempDirectory), "Completed exports must remove temporary archive and DICOM data.");
    }

    [TestMethod]
    public async Task ExportStudyMedia_RejectsOtherTenantBeforeS3Access()
    {
        await using var fixture = await ExportFixture.CreateAsync();
        fixture.TenantContext.SetTenant(Guid.NewGuid());
        using var s3 = new FakeS3Client();
        var controller = fixture.CreateController(s3, new UnusedExportService());

        var result = await controller.ExportStudyMedia(fixture.Study.Id, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundObjectResult>(result);
        Assert.AreEqual(0, s3.MetadataChecks.Count);
        Assert.AreEqual(0, s3.Downloads.Count);
    }

    [TestMethod]
    public async Task ExportStudyMedia_RejectsStudyWhoseClientIsNotOwnedByTenant()
    {
        await using var fixture = await ExportFixture.CreateAsync();
        await using (var context = fixture.CreateContext())
        {
            context.Tenants.Add(new Tenant
            {
                Id = Guid.NewGuid(),
                Name = "Other Tenant",
                Subdomain = $"other-{Guid.NewGuid():N}",
            });
            await context.SaveChangesAsync();
            var client = await context.Clients.SingleAsync(item => item.Id == fixture.Client.Id);
            client.TenantId = context.Tenants.Local.Single(tenant => tenant.Id != fixture.Tenant.Id).Id;
            await context.SaveChangesAsync();
        }

        using var s3 = new FakeS3Client();
        var controller = fixture.CreateController(s3, new UnusedExportService());
        var result = await controller.ExportStudyMedia(fixture.Study.Id, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundObjectResult>(result);
        Assert.AreEqual(0, s3.MetadataChecks.Count);
    }

    [TestMethod]
    public async Task ExportStudyMedia_MissingInstancePreventsPartialExport()
    {
        await using var fixture = await ExportFixture.CreateAsync();
        using var s3 = new FakeS3Client();
        s3.MissingKeys.Add("key-2");
        var controller = fixture.CreateController(s3, new UnusedExportService());

        var result = await controller.ExportStudyMedia(fixture.Study.Id, CancellationToken.None);

        Assert.IsInstanceOfType<ConflictObjectResult>(result);
        Assert.IsTrue(s3.MetadataChecks.Count > 0);
        Assert.AreEqual(0, s3.Downloads.Count);
    }

    [TestMethod]
    public async Task ExportStudyMedia_CleansTemporaryFilesWhenInstanceDisappearsAfterPreflight()
    {
        await using var fixture = await ExportFixture.CreateAsync();
        using var s3 = new FakeS3Client();
        for (var index = 1; index <= fixture.Instances.Count; index++)
        {
            s3.Objects[$"key-{index}"] = CreateDicomBytes(index, 1);
        }
        s3.MissingDuringDownload.Add("key-2");

        var service = fixture.CreateExportService(s3, new TestPackageProvider(fixture.CreateViewerPackage()));
        await Assert.ThrowsExceptionAsync<ImagingMediaExportException>(() =>
            service.CreateArchiveAsync(fixture.Instances, CancellationToken.None));

        Assert.AreEqual(0, GetFileCount(fixture.TempDirectory));
        Assert.AreEqual(0, GetDirectoryCount(fixture.TempDirectory));
    }

    [TestMethod]
    public async Task ExportStudyMedia_CleansTemporaryFilesWhenRequestIsCancelled()
    {
        await using var fixture = await ExportFixture.CreateAsync();
        using var s3 = new FakeS3Client();
        var packagePath = fixture.CreateViewerPackage();
        var delayedProvider = new DelayedPackageProvider(packagePath);
        var service = fixture.CreateExportService(s3, delayedProvider);
        using var cancellation = new CancellationTokenSource();

        var exportTask = service.CreateArchiveAsync(fixture.Instances, cancellation.Token);
        await delayedProvider.Started.Task;
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => exportTask);

        Assert.AreEqual(0, GetFileCount(fixture.TempDirectory));
        Assert.AreEqual(0, GetDirectoryCount(fixture.TempDirectory));
    }

    [TestMethod]
    public void CreateDownloadFileName_SanitizesAccessionAndUsesStudyIdWhenEmpty()
    {
        var studyId = Guid.NewGuid();

        Assert.AreEqual(
            "CLIENTA-AB12-CD-USB.zip",
            ImagingMediaExportService.CreateDownloadFileName("AB/12", studyId));
        Assert.AreEqual(
            $"CLIENTA-{studyId:N}-CD-USB.zip",
            ImagingMediaExportService.CreateDownloadFileName("💥", studyId));
    }

    private static byte[] CreateDicomBytes(int instance, int series)
    {
        var dataset = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian);
        dataset.Add(DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage);
        dataset.Add(DicomTag.SOPInstanceUID, $"1.2.826.0.1.3680043.10.999.{instance}");
        dataset.Add(DicomTag.StudyInstanceUID, "1.2.826.0.1.3680043.10.999.1");
        dataset.Add(DicomTag.SeriesInstanceUID, $"1.2.826.0.1.3680043.10.999.{series + 1}");
        dataset.Add(DicomTag.PatientID, "MEDIA-TEST");
        dataset.Add(DicomTag.PatientName, "Test^Patient");
        dataset.Add(DicomTag.Modality, "US");
        dataset.Add(DicomTag.StudyDate, "20260101");
        dataset.Add(DicomTag.SeriesNumber, series);
        dataset.Add(DicomTag.InstanceNumber, instance);

        var file = new DicomFile(dataset);
        using var stream = new MemoryStream();
        file.Save(stream);
        return stream.ToArray();
    }

    private static int GetFileCount(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory).Length : 0;

    private static int GetDirectoryCount(string directory) =>
        Directory.Exists(directory) ? Directory.GetDirectories(directory).Length : 0;

    private sealed class FakeS3Client : AmazonS3Client
    {
        public FakeS3Client() : base(new AnonymousAWSCredentials(), new AmazonS3Config
        {
            ServiceURL = "http://localhost",
            ForcePathStyle = true,
        }) { }

        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        public HashSet<string> MissingKeys { get; } = new(StringComparer.Ordinal);
        public HashSet<string> MissingDuringDownload { get; } = new(StringComparer.Ordinal);
        public List<string> MetadataChecks { get; } = [];
        public List<string> Downloads { get; } = [];

        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(
            GetObjectMetadataRequest request,
            CancellationToken cancellationToken = default)
        {
            MetadataChecks.Add(request.Key);
            if (MissingKeys.Contains(request.Key))
            {
                throw new AmazonS3Exception("Object not found") { StatusCode = HttpStatusCode.NotFound };
            }

            return Task.FromResult(new GetObjectMetadataResponse());
        }

        public override Task<GetObjectResponse> GetObjectAsync(
            GetObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            Downloads.Add(request.Key);
            if (MissingDuringDownload.Contains(request.Key) ||
                MissingKeys.Contains(request.Key) ||
                !Objects.TryGetValue(request.Key, out var bytes))
            {
                throw new AmazonS3Exception("Object not found") { StatusCode = HttpStatusCode.NotFound };
            }

            return Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(bytes, writable: false) });
        }
    }

    private sealed class TestPackageProvider(string packagePath) : IWeasisPortablePackageProvider
    {
        public Task<string> GetPackagePathAsync(CancellationToken cancellationToken) => Task.FromResult(packagePath);
    }

    private sealed class DelayedPackageProvider(string packagePath) : IWeasisPortablePackageProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> GetPackagePathAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return packagePath;
        }
    }

    private sealed class UnusedExportService : IImagingMediaExportService
    {
        public Task<Stream> CreateArchiveAsync(IReadOnlyList<ImagingInstance> instances, CancellationToken cancellationToken) =>
            throw new AssertFailedException("No export should be started for an unauthorized study.");
    }

    private sealed class ExportFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly string _packagePath;

        private ExportFixture(string databasePath, string tempDirectory, string packagePath)
        {
            _databasePath = databasePath;
            TempDirectory = tempDirectory;
            _packagePath = packagePath;
        }

        public required TestTenantContext TenantContext { get; init; }
        public required Tenant Tenant { get; init; }
        public required Client Client { get; init; }
        public required ImagingStudy Study { get; init; }
        public required List<ImagingInstance> Instances { get; set; }
        public string TempDirectory { get; }

        public static async Task<ExportFixture> CreateAsync()
        {
            var tenantContext = new TestTenantContext();
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = "Media Export Test",
                Subdomain = $"media-export-{Guid.NewGuid():N}",
            };
            var client = new Client { Id = Guid.NewGuid(), TenantId = tenant.Id, FullName = "Media Export Test Client" };
            var study = new ImagingStudy
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                ClientId = client.Id,
                AccessionNumber = "A-1",
                StudyInstanceUID = "1.2.826.0.1.3680043.10.999.1",
                Modality = "US",
                LocalStoragePath = string.Empty,
                StorageStatus = ImagingStudyStorageStatuses.LocalAndS3,
            };
            tenantContext.SetTenant(tenant.Id);
            tenantContext.SetUserId(Guid.NewGuid());

            var root = Path.Combine(Path.GetTempPath(), $"imaging-media-test-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(root, "test.db");
            var tempDirectory = Path.Combine(root, "work");
            var packagePath = Path.Combine(root, "viewer.zip");
            Directory.CreateDirectory(root);
            var fixture = new ExportFixture(databasePath, tempDirectory, packagePath)
            {
                TenantContext = tenantContext,
                Tenant = tenant,
                Client = client,
                Study = study,
                Instances = [],
            };

            await using var context = fixture.CreateContext();
            await context.Database.EnsureCreatedAsync();
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
            context.Clients.Add(client);
            await context.SaveChangesAsync();
            context.ImagingStudies.Add(study);
            await context.SaveChangesAsync();

            var series1 = new ImagingSeries
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                ImagingStudyId = study.Id,
                SeriesInstanceUID = "1.2.826.0.1.3680043.10.999.2",
                Modality = "US",
                SeriesNumber = 1,
            };
            var series2 = new ImagingSeries
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                ImagingStudyId = study.Id,
                SeriesInstanceUID = "1.2.826.0.1.3680043.10.999.3",
                Modality = "US",
                SeriesNumber = 2,
            };
            context.ImagingSeries.AddRange(series1, series2);

            var instances = new List<ImagingInstance>();
            for (var index = 1; index <= 3; index++)
            {
                var instance = new ImagingInstance
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    ImagingStudyId = study.Id,
                    ImagingSeriesId = index == 3 ? series2.Id : series1.Id,
                    SOPInstanceUID = $"1.2.826.0.1.3680043.10.999.{index}",
                    SOPClassUID = DicomUID.SecondaryCaptureImageStorage.UID,
                    InstanceNumber = index,
                    LocalFilePath = string.Empty,
                    StorageStatus = ImagingStudyStorageStatuses.LocalAndS3,
                    S3Bucket = "imaging-test-bucket",
                    S3Key = $"key-{index}",
                };
                instances.Add(instance);
            }
            context.ImagingInstances.AddRange(instances);
            await context.SaveChangesAsync();
            fixture.Instances = instances;
            return fixture;
        }

        public AppDbContext CreateContext() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_databasePath}").Options,
            TenantContext);

        public ImagingStudiesController CreateController(IAmazonS3 s3, IImagingMediaExportService exportService)
        {
            var context = CreateContext();
            var controller = new ImagingStudiesController(
                context,
                TenantContext,
                s3,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWS:ImagingBucketName"] = "imaging-test-bucket",
                }).Build(),
                NullLogger<ImagingStudiesController>.Instance,
                exportService,
                new TechnicianWorkSummaryService(context, TenantContext));
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } },
            };
            return controller;
        }

        public ImagingMediaExportService CreateExportService(IAmazonS3 s3, IWeasisPortablePackageProvider packageProvider)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImagingMediaExport:TemporaryDirectory"] = TempDirectory,
            }).Build();
            return new ImagingMediaExportService(
                s3,
                packageProvider,
                configuration,
                NullLogger<ImagingMediaExportService>.Instance);
        }

        public string CreateViewerPackage()
        {
            using var file = new FileStream(_packagePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            using (var executable = archive.CreateEntry("VIEWER/Weasis-4.8.0/Weasis.exe").Open())
            {
                executable.WriteByte(0x4d);
            }
            using (var license = archive.CreateEntry("LICENSES/EPL-2.0.txt").Open())
            {
                license.WriteByte(0x4c);
            }

            return _packagePath;
        }

        public async ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path.GetDirectoryName(_databasePath)))
            {
                Directory.Delete(Path.GetDirectoryName(_databasePath)!, recursive: true);
            }

            await Task.CompletedTask;
        }
    }
}
