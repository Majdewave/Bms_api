using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Compression;
using System.Net;
using System.Reflection;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class ImagingStudyDownloadTests
{
    [TestMethod]
    public async Task DownloadStudy_PreflightsAllInstances_AndStreamsOriginalBytesIntoUncompressedEntries()
    {
        await using var fixture = await DownloadFixture.CreateAsync(instanceCount: 2);
        using var s3 = new FakeS3Client();
        s3.Objects["key-1"] = [0, 1, 2, 255];
        s3.Objects["key-2"] = [10, 11, 12];
        var controller = fixture.CreateController(s3);

        var result = await controller.DownloadStudy(fixture.Study.Id, CancellationToken.None);

        var file = result as FileStreamResult;
        Assert.IsNotNull(file);
        Assert.AreEqual("application/zip", file.ContentType);
        Assert.AreEqual(2, s3.MetadataChecks.Count);
        Assert.AreEqual(2, s3.Downloads.Count);
        using var archive = new ZipArchive(file.FileStream, ZipArchiveMode.Read, leaveOpen: true);
        Assert.AreEqual(2, archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            Assert.IsTrue(entry.FullName.StartsWith("Series-1/", StringComparison.Ordinal));
            Assert.IsTrue(entry.FullName.EndsWith(".dcm", StringComparison.Ordinal));
            Assert.AreEqual(entry.Length, entry.CompressedLength, "DICOM entries should use no compression.");
        }
        using var firstEntry = archive.Entries.Single(entry => entry.FullName.Contains("sop-1", StringComparison.Ordinal)).Open();
        using var firstBytes = new MemoryStream();
        await firstEntry.CopyToAsync(firstBytes);
        CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 255 }, firstBytes.ToArray());
        file.FileStream.Dispose();
    }

    [TestMethod]
    public async Task DownloadStudy_UnavailableRegisteredInstanceReturnsConflictBeforeAnyGetObject()
    {
        await using var fixture = await DownloadFixture.CreateAsync(instanceCount: 2);
        using var s3 = new FakeS3Client();
        s3.MissingKeys.Add("key-2");
        s3.Objects["key-1"] = [1, 2, 3];
        var controller = fixture.CreateController(s3);

        var result = await controller.DownloadStudy(fixture.Study.Id, CancellationToken.None);

        Assert.IsInstanceOfType<ConflictObjectResult>(result);
        Assert.AreEqual(2, s3.MetadataChecks.Count);
        Assert.AreEqual(0, s3.Downloads.Count);
    }

    [TestMethod]
    public async Task DownloadStudy_OtherTenantCannotReadStudyOrAccessS3()
    {
        await using var fixture = await DownloadFixture.CreateAsync(instanceCount: 1);
        fixture.TenantContext.SetTenant(Guid.NewGuid());
        using var s3 = new FakeS3Client();
        var controller = fixture.CreateController(s3);

        var result = await controller.DownloadStudy(fixture.Study.Id, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundObjectResult>(result);
        Assert.AreEqual(0, s3.MetadataChecks.Count);
        Assert.AreEqual(0, s3.Downloads.Count);
    }

    private sealed class FakeS3Client : AmazonS3Client
    {
        public FakeS3Client() : base(new AnonymousAWSCredentials(), new AmazonS3Config
        {
            ServiceURL = "http://localhost",
            ForcePathStyle = true,
        }) { }

        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        public HashSet<string> MissingKeys { get; } = new(StringComparer.Ordinal);
        public List<string> MetadataChecks { get; } = [];
        public List<string> Downloads { get; } = [];

        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default)
        {
            MetadataChecks.Add(request.Key);
            if (MissingKeys.Contains(request.Key))
                throw new AmazonS3Exception("Object not found") { StatusCode = HttpStatusCode.NotFound };
            return Task.FromResult(new GetObjectMetadataResponse());
        }

        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            Downloads.Add(request.Key);
            if (MissingKeys.Contains(request.Key) || !Objects.TryGetValue(request.Key, out var bytes))
                throw new AmazonS3Exception("Object not found") { StatusCode = HttpStatusCode.NotFound };
            return Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(bytes, writable: false) });
        }
    }

    private sealed class DownloadFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private DownloadFixture(string databasePath) => _databasePath = databasePath;

        public required TestTenantContext TenantContext { get; init; }
        public required Tenant Tenant { get; init; }
        public required Client Client { get; init; }
        public required ImagingStudy Study { get; init; }

        public static async Task<DownloadFixture> CreateAsync(int instanceCount)
        {
            var tenantContext = new TestTenantContext();
            var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Study Download Test", Subdomain = $"study-download-{Guid.NewGuid():N}" };
            var client = new Client { Id = Guid.NewGuid(), TenantId = tenant.Id, FullName = "Download Test Client" };
            var study = new ImagingStudy
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, ClientId = client.Id,
                AccessionNumber = "A-1", StudyInstanceUID = "study-uid", Modality = "US",
                LocalStoragePath = string.Empty, StorageStatus = ImagingStudyStorageStatuses.LocalAndS3,
            };
            tenantContext.SetTenant(tenant.Id);
            tenantContext.SetUserId(Guid.NewGuid());
            var fixture = new DownloadFixture(Path.Combine(Path.GetTempPath(), $"study-download-{Guid.NewGuid():N}.db"))
            {
                TenantContext = tenantContext,
                Tenant = tenant,
                Client = client,
                Study = study,
            };

            await using var context = fixture.CreateContext();
            await context.Database.EnsureCreatedAsync();
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
            context.Clients.Add(client);
            await context.SaveChangesAsync();
            context.ImagingStudies.Add(study);
            await context.SaveChangesAsync();
            var series = new ImagingSeries
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, ImagingStudyId = study.Id,
                SeriesInstanceUID = "series-uid", Modality = "US", SeriesNumber = 1,
            };
            context.ImagingSeries.Add(series);
            for (var index = 1; index <= instanceCount; index++)
            {
                context.ImagingInstances.Add(new ImagingInstance
                {
                    Id = Guid.NewGuid(), TenantId = tenant.Id, ImagingStudyId = study.Id,
                    ImagingSeriesId = series.Id, SOPInstanceUID = $"sop-{index}", SOPClassUID = "dicom-class",
                    LocalFilePath = string.Empty, StorageStatus = ImagingStudyStorageStatuses.LocalAndS3,
                    S3Bucket = "imaging-test-bucket", S3Key = $"key-{index}",
                });
            }
            await context.SaveChangesAsync();
            return fixture;
        }

        public AppDbContext CreateContext() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_databasePath}").Options,
            TenantContext);

        public ImagingStudiesController CreateController(IAmazonS3 s3)
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
                NullLogger<ImagingStudiesController>.Instance);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } },
            };
            return controller;
        }

        public async ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
            await Task.CompletedTask;
        }
    }
}
