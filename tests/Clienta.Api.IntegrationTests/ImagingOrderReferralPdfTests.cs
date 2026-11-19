using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class ImagingOrderReferralPdfTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private sealed class CapturingLogger : ILogger<ImagingOrderReferralsController>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add($"{formatter(state, exception)} {exception}");
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SqliteConnection connection,
            AppDbContext db,
            ImagingOrderReferralsController controller,
            Guid orderId,
            CapturingLogger logger)
        {
            Connection = connection;
            Db = db;
            Controller = controller;
            OrderId = orderId;
            Logger = logger;
        }

        public SqliteConnection Connection { get; }
        public AppDbContext Db { get; }
        public ImagingOrderReferralsController Controller { get; }
        public Guid OrderId { get; }
        public CapturingLogger Logger { get; }

        public static async Task<Fixture> CreateAsync(CapturingLogger? logger = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);

            var tenantContext = new TestTenantContext
            {
                TenantId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
            };
            logger ??= new CapturingLogger();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, tenantContext);
            await db.Database.EnsureCreatedAsync();

            var clientId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = tenantContext.TenantId,
                Name = "Referral test",
                Subdomain = $"referral-{Guid.NewGuid():N}",
                ProductType = TenantProductType.Clinic,
                Plan = PlanType.Pro,
            });
            db.Users.Add(new User
            {
                Id = tenantContext.UserId.Value,
                TenantId = tenantContext.TenantId,
                Email = "referral-test@example.com",
                PasswordHash = "test-hash",
                Role = "admin",
            });
            db.Clients.Add(new Client
            {
                Id = clientId,
                TenantId = tenantContext.TenantId,
                FullName = "Referral client",
                Phone = "123",
            });
            db.ImagingOrders.Add(new ImagingOrder
            {
                Id = orderId,
                TenantId = tenantContext.TenantId,
                ClientId = clientId,
                AccessionNumber = "REF-TEST",
                Modality = "XR",
                Status = ImagingOrderStatuses.Scheduled,
                ScheduledStartTime = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            return new Fixture(
                connection,
                db,
                new ImagingOrderReferralsController(db, tenantContext, logger),
                orderId,
                logger);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task UploadDownloadReplaceAndDelete_PreservePdfMetadataAndManualImageBytes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var manualImageBytes = new byte[] { 0xff, 0xd8, 0xff, 0xd9 };

        var manualUpload = await fixture.Controller.UploadDocument(
            fixture.OrderId,
            MakeFile("manual-referral.jpg", "image/jpeg", manualImageBytes));
        Assert.IsInstanceOfType(manualUpload, typeof(OkObjectResult));
        var manualDocument = await fixture.Db.ImagingOrderDocuments.SingleAsync(document => !document.IsDeleted);
        CollectionAssert.AreEqual(manualImageBytes, manualDocument.FileData);
        Assert.AreEqual("image/jpeg", manualDocument.ContentType);

        var pdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nReferral PDF bytes\n%%EOF\n");
        var pdfUpload = await fixture.Controller.UploadDocument(
            fixture.OrderId,
            MakeFile("Referral.pdf", "application/pdf", pdfBytes));
        Assert.IsInstanceOfType(pdfUpload, typeof(OkObjectResult));

        var documents = await fixture.Db.ImagingOrderDocuments.ToListAsync();
        Assert.IsTrue(documents.Single(document => document.OriginalFileName == "manual-referral.jpg").IsDeleted);
        var current = documents.Single(document => !document.IsDeleted);
        Assert.AreEqual("Referral.pdf", current.OriginalFileName);
        Assert.AreEqual("application/pdf", current.ContentType);
        Assert.AreEqual((long)pdfBytes.Length, current.FileSize);
        CollectionAssert.AreEqual(pdfBytes, current.FileData);

        var download = await fixture.Controller.DownloadDocument(fixture.OrderId);
        Assert.IsInstanceOfType(download, typeof(FileContentResult));
        var downloadedFile = (FileContentResult)download;
        Assert.AreEqual("application/pdf", downloadedFile.ContentType);
        Assert.AreEqual("Referral.pdf", downloadedFile.FileDownloadName);
        CollectionAssert.AreEqual(pdfBytes, downloadedFile.FileContents);

        var delete = await fixture.Controller.DeleteDocument(fixture.OrderId);
        Assert.IsInstanceOfType(delete, typeof(NoContentResult));
        Assert.IsNull(await fixture.Db.ImagingOrderDocuments
            .FirstOrDefaultAsync(document => !document.IsDeleted));
        Assert.IsInstanceOfType(
            await fixture.Controller.DownloadDocument(fixture.OrderId),
            typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task UpdateTechnicianNotes_PersistsLineBreaksAndAllowsClearing()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string notes = "Patient had difficulty remaining still.\nPositioning limited the view.";

        var updated = await fixture.Controller.UpdateTechnicianNotes(
            fixture.OrderId,
            new UpdateImagingOrderTechnicianNotesRequest(notes),
            CancellationToken.None);

        Assert.IsInstanceOfType(updated, typeof(NoContentResult));
        Assert.AreEqual(notes, (await fixture.Db.ImagingOrders.SingleAsync()).TechnicianNotes);

        var cleared = await fixture.Controller.UpdateTechnicianNotes(
            fixture.OrderId,
            new UpdateImagingOrderTechnicianNotesRequest("  \n "),
            CancellationToken.None);

        Assert.IsInstanceOfType(cleared, typeof(NoContentResult));
        Assert.IsNull((await fixture.Db.ImagingOrders.SingleAsync()).TechnicianNotes);
    }

    [TestMethod]
    public async Task UpdateTechnicianNotes_LogsDatabaseFailureWithoutLoggingNoteContents()
    {
        var logger = new CapturingLogger();
        await using var fixture = await Fixture.CreateAsync(logger);
        const string noteContents = "private-note-sentinel";
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER FailTechnicianNotes BEFORE UPDATE OF TechnicianNotes ON ImagingOrders " +
            "BEGIN SELECT RAISE(FAIL, 'simulated database failure'); END;");

        await Assert.ThrowsExceptionAsync<DbUpdateException>(() =>
            fixture.Controller.UpdateTechnicianNotes(
                fixture.OrderId,
                new UpdateImagingOrderTechnicianNotesRequest(noteContents),
                CancellationToken.None));

        var log = string.Join(Environment.NewLine, logger.Messages);
        StringAssert.Contains(log, "Technician notes update failed during save");
        StringAssert.Contains(log, fixture.OrderId.ToString());
        StringAssert.Contains(log, fixture.Db.ImagingOrders.Single().TenantId.ToString());
        StringAssert.Contains(log, "DbUpdateException");
        Assert.IsFalse(log.Contains(noteContents, StringComparison.Ordinal));
    }

    private static IFormFile MakeFile(string name, string contentType, byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, stream.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }
}
