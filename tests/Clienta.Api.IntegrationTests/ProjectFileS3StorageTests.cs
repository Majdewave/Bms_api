using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class ProjectFileS3StorageTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private sealed class PmAccess : IPmAccessService
    {
        public Task<bool> IsPmEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class MemoryFileStorage : IFileStorage
    {
        public Dictionary<string, (byte[] Bytes, string ContentType)> Objects { get; } = new();
        public List<string> UploadedKeys { get; } = new();
        public List<string> DeletedKeys { get; } = new();

        public async Task<string> UploadAsync(Stream fileStream, string key, string contentType)
        {
            using var copy = new MemoryStream();
            await fileStream.CopyToAsync(copy);
            Objects[key] = (copy.ToArray(), contentType);
            UploadedKeys.Add(key);
            return $"https://storage.example/{key}"; // Service must never persist this returned URL.
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            Objects.TryGetValue(key, out var item)
                ? Task.FromResult<Stream>(new MemoryStream(item.Bytes, writable: false))
                : Task.FromException<Stream>(new FileNotFoundException());

        public Task DeleteAsync(string key)
        {
            DeletedKeys.Add(key);
            Objects.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class TestWebHost : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "project-file-test-wwwroot");
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "project-file-test-content");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly SqliteConnection Connection = new("Data Source=:memory:");
        public readonly TestTenantContext TenantContext = new();
        public readonly MemoryFileStorage Storage = new();
        public AppDbContext Db = null!;
        public ClientFileService Files = null!;
        public Guid TenantId;
        public Guid OtherTenantId;
        public Guid ClientId;
        public Guid OtherClientId;
        public Guid UserId;
        public Guid ProjectId;
        public Guid OtherProjectId;

        public async Task InitializeAsync()
        {
            await Connection.OpenAsync();
            Connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options;
            Db = new AppDbContext(options, TenantContext);
            await Db.Database.EnsureCreatedAsync();

            TenantId = Guid.NewGuid();
            OtherTenantId = Guid.NewGuid();
            ClientId = Guid.NewGuid();
            OtherClientId = Guid.NewGuid();
            UserId = Guid.NewGuid();
            ProjectId = Guid.NewGuid();
            OtherProjectId = Guid.NewGuid();
            TenantContext.SetTenant(TenantId);
            TenantContext.SetUserId(UserId);

            Db.Tenants.AddRange(
                new Tenant { Id = TenantId, Name = "Business", Subdomain = "b-" + Guid.NewGuid().ToString("N"), ProductType = TenantProductType.Business, Plan = PlanType.Pro, Currency = "ILS" },
                new Tenant { Id = OtherTenantId, Name = "Other", Subdomain = "o-" + Guid.NewGuid().ToString("N"), ProductType = TenantProductType.Business, Plan = PlanType.Pro, Currency = "ILS" });
            Db.Clients.AddRange(
                new Client { Id = ClientId, TenantId = TenantId, FullName = "Client A", Phone = "1" },
                new Client { Id = OtherClientId, TenantId = OtherTenantId, FullName = "Client B", Phone = "2" });
            Db.Users.Add(new User { Id = UserId, TenantId = TenantId, Email = "files@test.co", PasswordHash = "x", Role = "admin", FullName = "Tester" });
            Db.Projects.AddRange(
                new Project { Id = ProjectId, TenantId = TenantId, ClientId = ClientId, Name = "Project A", Status = ProjectStatus.Active },
                new Project { Id = OtherProjectId, TenantId = OtherTenantId, ClientId = OtherClientId, Name = "Project B", Status = ProjectStatus.Active });
            await Db.SaveChangesAsync();
            Files = new ClientFileService(Db, new TestWebHost(), Storage);
        }

        public ProjectFilesController CreateController() => new(
            Db, TenantContext, new PmAccess(), Files, NullLogger<ProjectFilesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private static FormFile UploadFile(string name, byte[] bytes, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private static async Task<Fixture> NewFixtureAsync()
    {
        var fixture = new Fixture();
        await fixture.InitializeAsync();
        return fixture;
    }

    [TestMethod]
    public async Task GenericProjectUpload_StoresInS3WithTenantProjectScopedKeyAndDownloadsFromS3()
    {
        await using var fixture = await NewFixtureAsync();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var result = await fixture.CreateController().Upload(fixture.ProjectId, UploadFile("scope.pdf", bytes, "application/pdf"), "Design file");

        var created = (CreatedAtActionResult)result;
        var row = await fixture.Db.ClientFiles.SingleAsync();
        Assert.AreEqual(fixture.TenantId, row.TenantId);
        Assert.AreEqual(fixture.ClientId, row.ClientId);
        Assert.AreEqual(fixture.ProjectId, row.ProjectId);
        Assert.AreEqual("scope.pdf", row.FileName);
        Assert.AreEqual("Design file", row.Title);
        Assert.AreEqual(bytes.Length, row.FileSize);
        Assert.AreEqual("S3", row.StorageProvider);
        Assert.IsTrue(row.StorageKey!.StartsWith($"projects/{fixture.TenantId:N}/{fixture.ProjectId:N}/{fixture.ClientId:N}/", StringComparison.Ordinal));
        Assert.IsFalse(row.StorageKey.Contains("https://", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(string.Empty, row.FilePath);
        Assert.AreEqual("application/pdf", fixture.Storage.Objects[row.StorageKey].ContentType);
        Assert.AreEqual(row.Id, ((Clienta.Api.DTOs.FileResponse)created.Value!).Id);

        var download = (FileStreamResult)await fixture.CreateController().Download(fixture.ProjectId, row.Id);
        using var content = new MemoryStream();
        await download.FileStream.CopyToAsync(content);
        CollectionAssert.AreEqual(bytes, content.ToArray());
    }

    [TestMethod]
    public async Task ProposalUpload_StoresInS3AndUsesExistingProposalRules()
    {
        await using var fixture = await NewFixtureAsync();
        var result = await fixture.CreateController().UploadProposal(
            fixture.ProjectId, UploadFile("offer.docx", new byte[] { 7, 8, 9 }, "application/octet-stream"), "  Offer  ");

        Assert.IsInstanceOfType(result, typeof(CreatedAtActionResult));
        var row = await fixture.Db.ClientFiles.SingleAsync();
        Assert.AreEqual("Offer", row.Title);
        Assert.AreEqual("offer.docx", row.FileName);
        Assert.AreEqual(fixture.ProjectId, row.ProjectId);
        Assert.AreEqual("S3", row.StorageProvider);
        Assert.IsTrue(row.StorageKey!.StartsWith($"projects/{fixture.TenantId:N}/{fixture.ProjectId:N}/{fixture.ClientId:N}/proposals/", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Storage.Objects.ContainsKey(row.StorageKey!));
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.wordprocessingml.document", fixture.Storage.Objects[row.StorageKey!].ContentType);

        var invalid = await fixture.CreateController().UploadProposal(
            fixture.ProjectId, UploadFile("offer.txt", new byte[] { 1 }, "text/plain"), "Offer");
        Assert.IsInstanceOfType(invalid, typeof(BadRequestObjectResult));
        var missingTitle = await fixture.CreateController().UploadProposal(
            fixture.ProjectId, UploadFile("offer.pdf", new byte[] { 1 }, "application/pdf"), "  ");
        Assert.IsInstanceOfType(missingTitle, typeof(BadRequestObjectResult));
        Assert.AreEqual(1, await fixture.Db.ClientFiles.CountAsync());
    }

    [TestMethod]
    public async Task ProposalPdfView_ReadsInlineContentFromS3()
    {
        await using var fixture = await NewFixtureAsync();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 1 };
        var controller = fixture.CreateController();
        var upload = (CreatedAtActionResult)await controller.UploadProposal(
            fixture.ProjectId, UploadFile("proposal.pdf", bytes, "application/pdf"), "Proposal");
        var fileId = ((Clienta.Api.DTOs.FileResponse)upload.Value!).Id;

        var view = (FileStreamResult)await controller.ViewProposal(fixture.ProjectId, fileId, CancellationToken.None);
        Assert.AreEqual("application/pdf", view.ContentType);
        Assert.IsTrue(string.IsNullOrEmpty(view.FileDownloadName));
        using var content = new MemoryStream();
        await view.FileStream.CopyToAsync(content);
        CollectionAssert.AreEqual(bytes, content.ToArray());
    }

    [TestMethod]
    public async Task DeleteProjectS3File_RemovesObjectAndClientFileRecord()
    {
        await using var fixture = await NewFixtureAsync();
        var controller = fixture.CreateController();
        await controller.Upload(fixture.ProjectId, UploadFile("delete.xlsx", new byte[] { 4, 5 }, "application/octet-stream"), "Sheet");
        var row = await fixture.Db.ClientFiles.SingleAsync();

        var result = await controller.Delete(fixture.ProjectId, row.Id);

        Assert.IsInstanceOfType(result, typeof(NoContentResult));
        Assert.IsFalse(fixture.Storage.Objects.ContainsKey(row.StorageKey!));
        CollectionAssert.Contains(fixture.Storage.DeletedKeys, row.StorageKey);
        Assert.AreEqual(0, await fixture.Db.ClientFiles.CountAsync());
    }

    [TestMethod]
    public async Task LegacyLocalProjectFile_RemainsReadableAndDeletable()
    {
        await using var fixture = await NewFixtureAsync();
        var localPath = Path.Combine(Path.GetTempPath(), $"legacy-project-{Guid.NewGuid():N}.pdf");
        var bytes = new byte[] { 9, 8, 7 };
        await File.WriteAllBytesAsync(localPath, bytes);
        var row = new ClientFile
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, ClientId = fixture.ClientId, ProjectId = fixture.ProjectId,
            FileName = "legacy.pdf", StoredFileName = "legacy.pdf", FilePath = localPath, FileSize = bytes.Length,
            UploadedByUserId = fixture.UserId, StorageProvider = null, StorageKey = null, Title = "Legacy"
        };
        fixture.Db.ClientFiles.Add(row);
        await fixture.Db.SaveChangesAsync();

        var download = (FileStreamResult)await fixture.CreateController().Download(fixture.ProjectId, row.Id);
        await using (download.FileStream)
        using (var content = new MemoryStream())
        {
            await download.FileStream.CopyToAsync(content);
            CollectionAssert.AreEqual(bytes, content.ToArray());
        }

        var delete = await fixture.CreateController().Delete(fixture.ProjectId, row.Id);
        Assert.IsInstanceOfType(delete, typeof(NoContentResult));
        Assert.IsFalse(File.Exists(localPath));
        Assert.AreEqual(0, await fixture.Db.ClientFiles.CountAsync());
        Assert.AreEqual(0, fixture.Storage.DeletedKeys.Count);
    }

    [TestMethod]
    public async Task ProjectFileRoutes_RejectForeignTenantProjectsAndOtherProjectsFiles()
    {
        await using var fixture = await NewFixtureAsync();
        var foreignProjectResult = await fixture.CreateController().List(fixture.OtherProjectId);
        Assert.IsInstanceOfType(foreignProjectResult, typeof(NotFoundObjectResult));

        // A same-tenant second project is included to ensure a file id alone is insufficient.
        var sameTenantProject = new Project { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ClientId = fixture.ClientId, Name = "Project A2", Status = ProjectStatus.Active };
        fixture.Db.Projects.Add(sameTenantProject);
        await fixture.Db.SaveChangesAsync();
        var upload = (CreatedAtActionResult)await fixture.CreateController().Upload(
            sameTenantProject.Id, UploadFile("other.pdf", new byte[] { 1 }, "application/pdf"), "Other");
        var fileId = ((Clienta.Api.DTOs.FileResponse)upload.Value!).Id;
        var wrongProjectDownload = await fixture.CreateController().Download(fixture.ProjectId, fileId);
        Assert.IsInstanceOfType(wrongProjectDownload, typeof(NotFoundObjectResult));

        var visible = (OkObjectResult)await fixture.CreateController().List(fixture.ProjectId);
        Assert.AreEqual(0, ((IReadOnlyCollection<Clienta.Api.DTOs.FileResponse>)visible.Value!).Count);
    }
}
