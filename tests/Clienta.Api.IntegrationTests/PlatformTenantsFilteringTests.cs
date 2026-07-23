using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Clienta.Api.Services.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class PlatformTenantsFilteringTests
{
    [TestMethod]
    public async Task GetTenants_Should_Return_200()
    {
        await using var fixture = await PlatformTenantsTestFixture.CreateAsync();
        await using var master = fixture.CreateMasterContext();
        await using var app = fixture.CreateAppContext();
        var controller = fixture.CreateController(master, app);

        var result = await controller.GetTenants(new PlatformTenantsQuery(), CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task GetTenants_Should_Return_Only_OnTrial_When_TrialEndsAt_Is_Future()
    {
        await using var fixture = await PlatformTenantsTestFixture.CreateAsync();
        await using var master = fixture.CreateMasterContext();
        await using var app = fixture.CreateAppContext();
        var controller = fixture.CreateController(master, app);

        var result = await controller.GetTenants(new PlatformTenantsQuery(Trial: "on_trial"), CancellationToken.None);

        var payload = (PlatformTenantsListResponseDto)((OkObjectResult)result).Value!;
        CollectionAssert.AreEqual(new[] { fixture.OnTrialBusinessName }, payload.Items.Select(x => x.BusinessName).ToArray());
    }

    [TestMethod]
    public async Task GetTenants_Should_Return_Only_Expired_When_TrialEndsAt_Is_Past()
    {
        await using var fixture = await PlatformTenantsTestFixture.CreateAsync();
        await using var master = fixture.CreateMasterContext();
        await using var app = fixture.CreateAppContext();
        var controller = fixture.CreateController(master, app);

        var result = await controller.GetTenants(new PlatformTenantsQuery(Trial: "expired"), CancellationToken.None);

        var payload = (PlatformTenantsListResponseDto)((OkObjectResult)result).Value!;
        CollectionAssert.AreEqual(new[] { fixture.ExpiredTrialBusinessName }, payload.Items.Select(x => x.BusinessName).ToArray());
    }

    [TestMethod]
    public async Task GetTenants_Should_Return_Approved_Businesses_Without_Pending_Or_Suspended_Records()
    {
        await using var fixture = await PlatformTenantsTestFixture.CreateAsync();
        await using var master = fixture.CreateMasterContext();
        await using var app = fixture.CreateAppContext();
        var controller = fixture.CreateController(master, app);

        var result = await controller.GetTenants(new PlatformTenantsQuery(Status: "approved"), CancellationToken.None);

        var payload = (PlatformTenantsListResponseDto)((OkObjectResult)result).Value!;

        var names = payload!.Items.Select(x => x.BusinessName).ToArray();
        CollectionAssert.Contains(names, fixture.OnTrialBusinessName);
        CollectionAssert.Contains(names, fixture.ActiveBusinessName);
        CollectionAssert.Contains(names, fixture.ExpiredTrialBusinessName);
        CollectionAssert.DoesNotContain(names, fixture.PendingBusinessName);
        CollectionAssert.DoesNotContain(names, fixture.SuspendedBusinessName);
    }

    [TestMethod]
    public async Task GetTenants_Should_Return_Pending_Businesses()
    {
        await using var fixture = await PlatformTenantsTestFixture.CreateAsync();
        await using var master = fixture.CreateMasterContext();
        await using var app = fixture.CreateAppContext();
        var controller = fixture.CreateController(master, app);

        var result = await controller.GetTenants(new PlatformTenantsQuery(Status: "pending"), CancellationToken.None);

        var payload = (PlatformTenantsListResponseDto)((OkObjectResult)result).Value!;
        CollectionAssert.AreEqual(new[] { fixture.PendingBusinessName }, payload.Items.Select(x => x.BusinessName).ToArray());
    }

    [TestMethod]
    public async Task GetTenants_Should_Return_Suspended_Businesses()
    {
        await using var fixture = await PlatformTenantsTestFixture.CreateAsync();
        await using var master = fixture.CreateMasterContext();
        await using var app = fixture.CreateAppContext();
        var controller = fixture.CreateController(master, app);

        var result = await controller.GetTenants(new PlatformTenantsQuery(Status: "suspended"), CancellationToken.None);

        var payload = (PlatformTenantsListResponseDto)((OkObjectResult)result).Value!;
        CollectionAssert.AreEqual(new[] { fixture.SuspendedBusinessName }, payload.Items.Select(x => x.BusinessName).ToArray());
    }

    [TestMethod]
    public async Task GetTenants_Should_Combine_Approved_And_OnTrial_Filters()
    {
        await using var fixture = await PlatformTenantsTestFixture.CreateAsync();
        await using var master = fixture.CreateMasterContext();
        await using var app = fixture.CreateAppContext();
        var controller = fixture.CreateController(master, app);

        var result = await controller.GetTenants(new PlatformTenantsQuery(Status: "approved", Trial: "on_trial"), CancellationToken.None);

        var payload = (PlatformTenantsListResponseDto)((OkObjectResult)result).Value!;
        CollectionAssert.AreEqual(new[] { fixture.OnTrialBusinessName }, payload.Items.Select(x => x.BusinessName).ToArray());
    }
}

internal sealed class PlatformTenantsTestFixture : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    private PlatformTenantsTestFixture(string databasePath)
    {
        _databasePath = databasePath;
        _connectionString = $"Data Source={databasePath}";
        TenantContext = new TestTenantContext();
    }

    public TestTenantContext TenantContext { get; }
    public string OnTrialBusinessName { get; } = "On Trial Business";
    public string ExpiredTrialBusinessName { get; } = "Expired Trial Business";
    public string ActiveBusinessName { get; } = "Active Approved Business";
    public string PendingBusinessName { get; } = "Pending Business";
    public string SuspendedBusinessName { get; } = "Suspended Business";

    public static async Task<PlatformTenantsTestFixture> CreateAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"clienta-platform-tenants-tests-{Guid.NewGuid():N}.db");
        var fixture = new PlatformTenantsTestFixture(databasePath);

        await using var context = fixture.CreateAppContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        await fixture.SeedAsync(context);

        return fixture;
    }

    public AppDbContext CreateAppContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        return new AppDbContext(options, TenantContext);
    }

    public MasterDbContext CreateMasterContext()
    {
        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        return new MasterDbContext(options);
    }

    public PlatformTenantsController CreateController(MasterDbContext master, AppDbContext app)
    {
        var service = new PlatformTenantManagementService(master, app, new NoOpTenantApprovalService());
        return new PlatformTenantsController(service);
    }

    private async Task SeedAsync(AppDbContext context)
    {
        var now = DateTime.UtcNow;

        var onTrialTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = OnTrialBusinessName,
            Subdomain = $"trial-{Guid.NewGuid():N}",
            Plan = PlanType.Pro,
            SubscriptionStatus = SubscriptionStatus.Active,
            TrialEndsAt = now.AddDays(7),
            IsTrial = false,
            CreatedAt = now.AddDays(-3),
            LegalBusinessName = "On Trial Legal Name",
        };

        var expiredTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = ExpiredTrialBusinessName,
            Subdomain = $"expired-{Guid.NewGuid():N}",
            Plan = PlanType.Pro,
            SubscriptionStatus = SubscriptionStatus.GracePeriod,
            TrialEndsAt = now.AddDays(-2),
            IsTrial = false,
            CreatedAt = now.AddDays(-10),
            LegalBusinessName = "Expired Trial Legal Name",
        };

        var activeTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = ActiveBusinessName,
            Subdomain = $"active-{Guid.NewGuid():N}",
            Plan = PlanType.Pro,
            SubscriptionStatus = SubscriptionStatus.Active,
            CreatedAt = now.AddDays(-5),
            LegalBusinessName = "Active Legal Name",
        };

        var pendingTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = PendingBusinessName,
            Subdomain = $"pending-{Guid.NewGuid():N}",
            Plan = PlanType.Trial,
            SubscriptionStatus = SubscriptionStatus.PendingApproval,
            CreatedAt = now.AddDays(-1),
        };

        var suspendedTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = SuspendedBusinessName,
            Subdomain = $"suspended-{Guid.NewGuid():N}",
            Plan = PlanType.Pro,
            SubscriptionStatus = SubscriptionStatus.Canceled,
            IsSuspended = true,
            CreatedAt = now.AddDays(-4),
            LegalBusinessName = "Suspended Legal Name",
        };

        context.Tenants.AddRange(onTrialTenant, expiredTenant, activeTenant, pendingTenant, suspendedTenant);
        await context.SaveChangesAsync();

        var users = new[]
        {
            CreateOwnerUser(onTrialTenant.Id, "ontrial-owner@test.local"),
            CreateOwnerUser(expiredTenant.Id, "expired-owner@test.local"),
            CreateOwnerUser(activeTenant.Id, "active-owner@test.local"),
            CreateOwnerUser(pendingTenant.Id, "pending-owner@test.local"),
            CreateOwnerUser(suspendedTenant.Id, "suspended-owner@test.local"),
        };

        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        onTrialTenant.OwnerUserId = users[0].Id;
        expiredTenant.OwnerUserId = users[1].Id;
        activeTenant.OwnerUserId = users[2].Id;
        pendingTenant.OwnerUserId = users[3].Id;
        suspendedTenant.OwnerUserId = users[4].Id;

        await context.SaveChangesAsync();
    }

    private static User CreateOwnerUser(Guid tenantId, string email)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "hash",
            Role = "Owner",
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow.AddDays(-20),
            IsActive = true,
        };
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests.
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class NoOpTenantApprovalService : ITenantApprovalService
{
    public Task ApproveTenantAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
}