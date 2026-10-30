using Amazon.S3;
using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Claims;
using System.Text.Json;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class InterpreterMarkerAnnotationTests
{
    [TestMethod]
    public async Task AssignedInterpreter_CreatesSharedMarker_VisibleFromClinicAnnotations()
    {
        await using var fixture = await MarkerFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context, fixture.Interpreter.Id);
        var request = new CreateImagingAnnotationRequest(
            fixture.Instance.Id, 1, "marker-uid", "LMarker",
            JsonDocument.Parse("{\"data\":{\"handles\":{\"points\":[[1,2,0]]}},\"metadata\":{}}" ).RootElement,
            "L");

        var created = await controller.CreateInstanceMarker(fixture.Request.Id, fixture.Instance.Id, request, CancellationToken.None);
        Assert.IsInstanceOfType<OkObjectResult>(created);

        var annotation = await context.ImagingAnnotations.SingleAsync();
        Assert.AreEqual("LMarker", annotation.ToolName);
        Assert.AreEqual(fixture.Instance.Id, annotation.ImagingInstanceId);
        Assert.AreEqual(fixture.Interpreter.Id, annotation.CreatedByUserId);

        var sharedView = new ImagingAnnotationsController(context, fixture.TenantContext, NullLogger<ImagingAnnotationsController>.Instance);
        var loaded = await sharedView.GetAnnotations(fixture.Instance.Id, 1, CancellationToken.None);
        Assert.IsInstanceOfType<OkObjectResult>(loaded);
        Assert.AreEqual(1, ((IEnumerable<ImagingAnnotationDto>)((OkObjectResult)loaded).Value!).Count());

        var interpreterView = await controller.GetInstanceAnnotations(fixture.Request.Id, fixture.Instance.Id, 1, CancellationToken.None);
        Assert.IsInstanceOfType<OkObjectResult>(interpreterView);
        var visibleMarker = ((IEnumerable<ImagingAnnotationDto>)((OkObjectResult)interpreterView).Value!).Single();
        Assert.AreEqual(annotation.Id, visibleMarker.Id);
    }

    [TestMethod]
    public async Task InterpreterMarkerWrites_RequireAssignedRequestAndMarkerOnlyRows()
    {
        await using var fixture = await MarkerFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context, fixture.Interpreter.Id);
        var payload = JsonDocument.Parse("{\"data\":{\"label\":\"L\"},\"metadata\":{}}" ).RootElement;
        var create = new CreateImagingAnnotationRequest(fixture.Instance.Id, 1, "marker-uid", "LMarker", payload, "L");

        var wrongAssignment = await controller.CreateInstanceMarker(Guid.NewGuid(), fixture.Instance.Id, create, CancellationToken.None);
        Assert.IsInstanceOfType<NotFoundResult>(wrongAssignment);

        context.ImagingAnnotations.Add(new ImagingAnnotation
        {
            Id = Guid.NewGuid(), TenantId = fixture.Tenant.Id, ImagingInstanceId = fixture.Instance.Id,
            FrameNumber = 1, AnnotationUid = "measurement-uid", ToolName = "Length",
            Geometry = "{}", CreatedByUserId = fixture.Interpreter.Id,
        });
        await context.SaveChangesAsync();
        var nonMarkerId = await context.ImagingAnnotations.Select(item => item.Id).SingleAsync();
        var rejectedMutation = await controller.DeleteInstanceMarker(fixture.Request.Id, nonMarkerId, CancellationToken.None);
        Assert.IsInstanceOfType<NotFoundResult>(rejectedMutation);

        var result = await controller.CreateInstanceMarker(fixture.Request.Id, fixture.Instance.Id,
            create with { ToolName = "ArrowAnnotate", Label = "L" }, CancellationToken.None);
        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    [TestMethod]
    public async Task InterpreterCannotMutateMarkerOutsideAssignedStudy()
    {
        await using var fixture = await MarkerFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var otherStudy = new ImagingStudy
        {
            Id = Guid.NewGuid(), TenantId = fixture.Tenant.Id, ClientId = fixture.Client.Id,
            AccessionNumber = "A2", StudyInstanceUID = "study-2", Modality = "XR",
            LocalStoragePath = "", StorageStatus = ImagingStudyStorageStatuses.Local,
        };
        context.ImagingStudies.Add(otherStudy);
        var otherSeries = new ImagingSeries
        {
            Id = Guid.NewGuid(), TenantId = fixture.Tenant.Id, ImagingStudyId = otherStudy.Id,
            SeriesInstanceUID = "series-2", Modality = "XR",
        };
        context.ImagingSeries.Add(otherSeries);
        var otherInstance = new ImagingInstance
        {
            Id = Guid.NewGuid(), TenantId = fixture.Tenant.Id, ImagingStudyId = otherStudy.Id,
            ImagingSeriesId = otherSeries.Id, SOPInstanceUID = "instance-2", SOPClassUID = "class",
            LocalFilePath = "", StorageStatus = ImagingStudyStorageStatuses.Local,
        };
        context.ImagingInstances.Add(otherInstance);
        var marker = new ImagingAnnotation
        {
            Id = Guid.NewGuid(), TenantId = fixture.Tenant.Id, ImagingInstanceId = otherInstance.Id,
            FrameNumber = 1, AnnotationUid = "outside-marker", ToolName = "RMarker", Label = "R",
            Geometry = "{}", CreatedByUserId = fixture.Interpreter.Id,
        };
        context.ImagingAnnotations.Add(marker);
        await context.SaveChangesAsync();

        var controller = fixture.CreateController(context, fixture.Interpreter.Id);
        var update = await controller.UpdateInstanceMarker(fixture.Request.Id, marker.Id,
            new UpdateImagingAnnotationRequest(JsonDocument.Parse("{\"data\":{}}" ).RootElement, "R"), CancellationToken.None);
        var delete = await controller.DeleteInstanceMarker(fixture.Request.Id, marker.Id, CancellationToken.None);
        Assert.IsInstanceOfType<NotFoundResult>(update);
        Assert.IsInstanceOfType<NotFoundResult>(delete);
        Assert.IsTrue(await context.ImagingAnnotations.AnyAsync(item => item.Id == marker.Id));
    }

    [TestMethod]
    public async Task MarkerWrites_RequireInterpreterRole()
    {
        await using var fixture = await MarkerFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context, fixture.Interpreter.Id, "Owner");
        var payload = JsonDocument.Parse("{\"data\":{}}" ).RootElement;
        var result = await controller.CreateInstanceMarker(fixture.Request.Id, fixture.Instance.Id,
            new CreateImagingAnnotationRequest(fixture.Instance.Id, 1, "unauthorized", "LMarker", payload, "L"),
            CancellationToken.None);

        Assert.IsInstanceOfType<ForbidResult>(result);
        Assert.AreEqual(0, await context.ImagingAnnotations.CountAsync());
    }
}

internal sealed class MarkerFixture : IAsyncDisposable
{
    private readonly string _dbPath;
    private MarkerFixture(string dbPath) => _dbPath = dbPath;

    public required Tenant Tenant { get; init; }
    public required User Interpreter { get; init; }
    public required Client Client { get; init; }
    public required ImagingStudy Study { get; init; }
    public required ImagingSeries Series { get; init; }
    public required ImagingInstance Instance { get; init; }
    public required InterpretationRequest Request { get; init; }
    public required TestTenantContext TenantContext { get; init; }

    public static async Task<MarkerFixture> CreateAsync()
    {
        var fixture = new MarkerFixture(Path.Combine(Path.GetTempPath(), $"marker-tests-{Guid.NewGuid():N}.db"))
        {
            TenantContext = new TestTenantContext(),
            Tenant = new Tenant { Id = Guid.NewGuid(), Name = "Marker Tenant", Subdomain = $"marker-{Guid.NewGuid():N}" },
            Interpreter = new User { Id = Guid.NewGuid(), TenantId = Guid.Empty, Email = "interpreter@test.local", PasswordHash = "hash", Role = "Interpreter" },
            Client = new Client { Id = Guid.NewGuid(), TenantId = Guid.Empty, FullName = "Marker Client" },
            Study = new ImagingStudy { Id = Guid.NewGuid(), TenantId = Guid.Empty, ClientId = Guid.Empty, AccessionNumber = "A1", StudyInstanceUID = "study-1", Modality = "XR", LocalStoragePath = "", StorageStatus = ImagingStudyStorageStatuses.Local },
            Series = new ImagingSeries { Id = Guid.NewGuid(), TenantId = Guid.Empty, ImagingStudyId = Guid.Empty, SeriesInstanceUID = "series-1", Modality = "XR" },
            Instance = new ImagingInstance { Id = Guid.NewGuid(), TenantId = Guid.Empty, ImagingStudyId = Guid.Empty, ImagingSeriesId = Guid.Empty, SOPInstanceUID = "instance-1", SOPClassUID = "class", LocalFilePath = "", StorageStatus = ImagingStudyStorageStatuses.Local },
            Request = new InterpretationRequest { Id = Guid.NewGuid(), TenantId = Guid.Empty, ImagingOrderId = Guid.NewGuid(), ImagingStudyId = Guid.Empty, AssignedInterpreterId = Guid.Empty, RequestedByUserId = Guid.Empty },
        };
        fixture.Interpreter.TenantId = fixture.Tenant.Id;
        fixture.Client.TenantId = fixture.Tenant.Id;
        fixture.Study.TenantId = fixture.Tenant.Id;
        fixture.Study.ClientId = fixture.Client.Id;
        fixture.Series.TenantId = fixture.Tenant.Id;
        fixture.Series.ImagingStudyId = fixture.Study.Id;
        fixture.Instance.TenantId = fixture.Tenant.Id;
        fixture.Instance.ImagingStudyId = fixture.Study.Id;
        fixture.Instance.ImagingSeriesId = fixture.Series.Id;
        fixture.Request.TenantId = fixture.Tenant.Id;
        fixture.Request.ImagingStudyId = fixture.Study.Id;
        fixture.Request.AssignedInterpreterId = fixture.Interpreter.Id;
        fixture.Request.RequestedByUserId = fixture.Interpreter.Id;
        fixture.TenantContext.SetTenant(fixture.Tenant.Id);
        fixture.TenantContext.SetUserId(fixture.Interpreter.Id);

        await using var context = fixture.CreateContext();
        await context.Database.EnsureCreatedAsync();
        context.Tenants.Add(fixture.Tenant);
        await context.SaveChangesAsync();
        context.Users.Add(fixture.Interpreter);
        context.Clients.Add(fixture.Client);
        await context.SaveChangesAsync();
        fixture.Tenant.OwnerUserId = fixture.Interpreter.Id;
        await context.SaveChangesAsync();
        context.ImagingStudies.Add(fixture.Study);
        await context.SaveChangesAsync();
        context.ImagingSeries.Add(fixture.Series);
        await context.SaveChangesAsync();
        context.ImagingInstances.Add(fixture.Instance);
        context.ImagingOrders.Add(new ImagingOrder
        {
            Id = fixture.Request.ImagingOrderId, TenantId = fixture.Tenant.Id, ClientId = fixture.Client.Id,
            AccessionNumber = "A1", Modality = "XR", ScheduledStartTime = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
        context.InterpretationRequests.Add(fixture.Request);
        await context.SaveChangesAsync();
        return fixture;
    }

    public AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options,
        TenantContext);

    public InterpreterPortalController CreateController(AppDbContext context, Guid actingUserId, string role = "Interpreter")
    {
        TenantContext.SetUserId(actingUserId);
        var controller = new InterpreterPortalController(
            context, TenantContext, (IAmazonS3)null!, NullLogger<InterpreterPortalController>.Instance,
            new ConfigurationBuilder().Build(),
            new InterpretationPdfService((IAmazonS3)null!, new ConfigurationBuilder().Build(), NullLogger<InterpretationPdfService>.Instance));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, role),
                }, "test")),
            },
        };
        return controller;
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }
}