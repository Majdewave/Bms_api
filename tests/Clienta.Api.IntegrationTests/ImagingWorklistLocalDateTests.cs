using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class ImagingWorklistLocalDateTests
{
    [TestMethod]
    public async Task LocalDate_Filters_Legacy_WallClock_Day_And_Excludes_Previous_Calendar_Day()
    {
        await using var fixture = await WorklistFixture.CreateAsync();
        var midnightOrder = fixture.CreateOrder(new DateTime(2026, 11, 8, 1, 24, 0, DateTimeKind.Utc), "DX");
        var daytimeOrder = fixture.CreateOrder(new DateTime(2026, 11, 8, 14, 30, 0, DateTimeKind.Utc), "DX");
        var previousDayOrder = fixture.CreateOrder(new DateTime(2026, 11, 7, 23, 24, 0, DateTimeKind.Utc), "DX");

        await using (var context = fixture.CreateContext())
        {
            context.ImagingOrders.AddRange(midnightOrder, daytimeOrder, previousDayOrder);
            await context.SaveChangesAsync();
        }

        await using var queryContext = fixture.CreateContext();
        var controller = new ImagingWorklistController(
            queryContext,
            fixture.TenantContext,
            new EnableAllDepartmentFeatures());

        var nov8Result = await controller.GetWorklist("DX", null, null, "2026-11-08", CancellationToken.None);
        var nov8Items = GetItems(nov8Result);

        Assert.AreEqual(2, nov8Items.Count);
        Assert.IsTrue(nov8Items.Any(item => item.ImagingOrderId == midnightOrder.Id));
        Assert.IsTrue(nov8Items.Any(item => item.ImagingOrderId == daytimeOrder.Id));
        Assert.IsFalse(nov8Items.Any(item => item.ImagingOrderId == previousDayOrder.Id));
        Assert.AreEqual(new DateTime(2026, 11, 8, 1, 24, 0),
            nov8Items.Single(item => item.ImagingOrderId == midnightOrder.Id).ScheduledStartTime);
        Assert.AreEqual(new DateTime(2026, 11, 8, 14, 30, 0),
            nov8Items.Single(item => item.ImagingOrderId == daytimeOrder.Id).ScheduledStartTime);

        var nov7Result = await controller.GetWorklist("DX", null, null, "2026-11-07", CancellationToken.None);
        var nov7Items = GetItems(nov7Result);
        Assert.IsFalse(nov7Items.Any(item => item.ImagingOrderId == midnightOrder.Id));
        Assert.IsTrue(nov7Items.Any(item => item.ImagingOrderId == previousDayOrder.Id));
    }

    [TestMethod]
    public async Task LocalDate_Rejects_Invalid_Or_Mixed_Date_Filters()
    {
        await using var fixture = await WorklistFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = new ImagingWorklistController(
            context,
            fixture.TenantContext,
            new EnableAllDepartmentFeatures());

        var invalidDate = await controller.GetWorklist("DX", null, null, "20261108", CancellationToken.None);
        var mixedFilters = await controller.GetWorklist(
            "DX",
            new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero),
            null,
            "2026-11-08",
            CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(invalidDate);
        Assert.IsInstanceOfType<BadRequestObjectResult>(mixedFilters);
    }

    private static List<ImagingWorklistItemDto> GetItems(IActionResult result)
    {
        Assert.IsInstanceOfType<OkObjectResult>(result);
        var value = ((OkObjectResult)result).Value;
        Assert.IsInstanceOfType<List<ImagingWorklistItemDto>>(value);
        return (List<ImagingWorklistItemDto>)value!;
    }

    private sealed class EnableAllDepartmentFeatures : IDepartmentFeatureResolver
    {
        public Task<bool> IsFeatureEnabledAsync(Guid tenantId, Guid? departmentId, string featureKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<List<DepartmentFeatureItemResponse>> GetDepartmentFeaturesAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateDepartmentFeaturesAsync(Guid tenantId, Guid departmentId, IReadOnlyCollection<DepartmentFeatureUpdateItem> updates, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class WorklistFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private WorklistFixture(string databasePath)
        {
            _databasePath = databasePath;
        }

        public required Tenant Tenant { get; init; }
        public required Client Client { get; init; }
        public required TestTenantContext TenantContext { get; init; }

        public static async Task<WorklistFixture> CreateAsync()
        {
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = "MWL Local Date Test",
                Subdomain = $"mwl-local-date-{Guid.NewGuid():N}",
            };
            var client = new Client
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                FullName = "MWL Test Patient",
            };
            var tenantContext = new TestTenantContext();
            tenantContext.SetTenant(tenant.Id);
            var fixture = new WorklistFixture(Path.Combine(Path.GetTempPath(), $"mwl-local-date-{Guid.NewGuid():N}.db"))
            {
                Tenant = tenant,
                Client = client,
                TenantContext = tenantContext,
            };

            await using var context = fixture.CreateContext();
            await context.Database.EnsureCreatedAsync();
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
            context.Clients.Add(client);
            await context.SaveChangesAsync();
            return fixture;
        }

        public ImagingOrder CreateOrder(DateTime scheduledStartTime, string modality)
            => new()
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant.Id,
                ClientId = Client.Id,
                AccessionNumber = $"MWL-{Guid.NewGuid():N}",
                Modality = modality,
                Status = ImagingOrderStatuses.Scheduled,
                ScheduledStartTime = scheduledStartTime,
            };

        public AppDbContext CreateContext()
            => new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_databasePath}")
                .Options, TenantContext);

        public ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_databasePath))
                File.Delete(_databasePath);
            return ValueTask.CompletedTask;
        }
    }
}