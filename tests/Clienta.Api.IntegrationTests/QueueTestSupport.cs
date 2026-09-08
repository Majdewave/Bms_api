using System.Collections.Concurrent;
using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Hubs;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

internal static class QueueTestHelpers
{
    internal static async Task<AppointmentDto> CreateScheduledAppointmentAsync(
        AppointmentsController controller,
        Guid clientId,
        DateTime startUtc)
    {
        var result = await controller.Create(new CreateAppointmentRequest(
            clientId,
            startUtc,
            startUtc.AddMinutes(30),
            "note",
            null,
            null,
            true));

        Assert.IsInstanceOfType<CreatedAtActionResult>(result);
        var created = (CreatedAtActionResult)result;
        Assert.IsInstanceOfType<AppointmentDto>(created.Value);
        return (AppointmentDto)created.Value!;
    }

    internal static async Task MoveToWaitingAsync(AppointmentsController controller, AppDbContext context, Guid appointmentId)
    {
        var entity = await context.Appointments.FirstAsync(a => a.Id == appointmentId);
        var update = await controller.Update(appointmentId, new UpdateAppointmentRequest(
            default,
            default,
            AppointmentStatuses.Waiting,
            entity.Notes,
            entity.ServiceId,
            entity.StaffId,
            entity.IsDocumented,
            null));

        Assert.IsInstanceOfType<OkObjectResult>(update);
    }

    internal static async Task<List<Appointment>> SeedWaitingQueueAsync(
        AppointmentsController controller,
        AppDbContext context,
        Guid clientId,
        int count)
    {
        var created = new List<AppointmentDto>();
        for (var i = 0; i < count; i++)
        {
            var dto = await CreateScheduledAppointmentAsync(
                controller,
                clientId,
                new DateTime(2026, 7, 20, 9 + i, 0, 0, DateTimeKind.Utc));
            created.Add(dto);
        }

        foreach (var dto in created)
        {
            await MoveToWaitingAsync(controller, context, dto.Id);
        }

        return await context.Appointments
            .Where(a => created.Select(c => c.Id).Contains(a.Id))
            .ToListAsync();
    }
}

internal sealed class QueueTestFixture : IAsyncDisposable
{
    private readonly string _connectionString;

    private QueueTestFixture(string databaseFilePath)
    {
        _connectionString = $"Data Source={databaseFilePath}";
    }

    public required Tenant Tenant { get; init; }
    public required User User { get; init; }
    public required Client Client { get; init; }
    public required TestTenantContext TenantContext { get; init; }
    public required RecordingHubContext HubContext { get; init; }
    public required string DatabaseFilePath { get; init; }

    public static async Task<QueueTestFixture> CreateAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clienta-queue-tests-{Guid.NewGuid():N}.db");
        var fixture = new QueueTestFixture(dbPath)
        {
            TenantContext = new TestTenantContext(),
            HubContext = new RecordingHubContext(),
            DatabaseFilePath = dbPath,
            Tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = "Queue Test Tenant",
                Subdomain = $"queue-{Guid.NewGuid():N}",
                Plan = PlanType.Pro,
                SubscriptionStatus = SubscriptionStatus.Active,
            },
            User = new User
            {
                Id = Guid.NewGuid(),
                Email = $"owner-{Guid.NewGuid():N}@test.local",
                PasswordHash = "hash",
                Role = "Owner",
                TenantId = Guid.Empty,
            },
            Client = new Client
            {
                Id = Guid.NewGuid(),
                FullName = "Queue Test Client",
                TenantId = Guid.Empty,
            }
        };

        fixture.User.TenantId = fixture.Tenant.Id;
        fixture.Client.TenantId = fixture.Tenant.Id;

        fixture.TenantContext.SetTenant(fixture.Tenant.Id);
        fixture.TenantContext.SetUserId(fixture.User.Id);

        await using var context = fixture.CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        context.Tenants.Add(fixture.Tenant);
        await context.SaveChangesAsync();

        context.Users.Add(fixture.User);
        await context.SaveChangesAsync();

        fixture.Tenant.OwnerUserId = fixture.User.Id;
        await context.SaveChangesAsync();

        context.Clients.Add(fixture.Client);
        await context.SaveChangesAsync();

        return fixture;
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        return new AppDbContext(options, TenantContext);
    }

    public AppointmentsController CreateController(AppDbContext context)
    {
        return new AppointmentsController(
            context,
            TenantContext,
            new AllowAllDepartmentAccessService(TenantContext),
            new PassThroughPlanEnforcementService(),
            new TestImagingAccessionNumberGenerator(),
            HubContext,
            new AllowAllUserDepartmentFeatureAccessService(),
            new NoOpQueueDisplayHubContext());
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(DatabaseFilePath))
            {
                File.Delete(DatabaseFilePath);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests.
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class TestTenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        TenantId = tenantId;
    }

    public void SetUserId(Guid userId)
    {
        UserId = userId;
    }
}

internal sealed class AllowAllDepartmentAccessService : IDepartmentAccessService
{
    private readonly ITenantContext _tenantContext;

    public AllowAllDepartmentAccessService(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public Task<DepartmentAccessContext> GetCurrentUserAccessContextAsync()
    {
        return Task.FromResult(new DepartmentAccessContext
        {
            TenantId = _tenantContext.TenantId,
            UserId = _tenantContext.UserId ?? Guid.Empty,
            IsOwner = true,
        });
    }

    public IQueryable<Appointment> ApplyAppointmentVisibility(IQueryable<Appointment> query, DepartmentAccessContext accessContext)
    {
        return query;
    }

    public IQueryable<VisitSummary> ApplyVisitSummaryVisibility(IQueryable<VisitSummary> query, DepartmentAccessContext accessContext)
    {
        return query;
    }
}

internal sealed class PassThroughPlanEnforcementService : IPlanEnforcementService
{
    public Task EnsureUserLimitAsync(Guid tenantId) => Task.CompletedTask;
    public Task EnsureMessageLimitAsync(Guid tenantId) => Task.CompletedTask;
    public Task<bool> CanCreateUserAsync(Guid tenantId) => Task.FromResult(true);
    public Task<bool> CanSendMessageAsync(Guid tenantId) => Task.FromResult(true);
}

internal sealed class RecordingHubContext : IHubContext<AppointmentsHub>
{
    public RecordingHubClients HubClients { get; } = new();
    public IHubClients Clients => HubClients;
    public IGroupManager Groups { get; } = new NoOpGroupManager();
}

internal sealed class RecordingHubClients : IHubClients
{
    private readonly RecordingClientProxy _proxy = new();

    public RecordingClientProxy Proxy => _proxy;

    public IClientProxy All => _proxy;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
    public IClientProxy Client(string connectionId) => _proxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
    public IClientProxy Group(string groupName) => _proxy;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
    public IClientProxy User(string userId) => _proxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
}

internal sealed class RecordingClientProxy : IClientProxy
{
    public ConcurrentBag<string> Methods { get; } = new();

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        Methods.Add(method);
        return Task.CompletedTask;
    }
}

internal sealed class NoOpGroupManager : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class TestImagingAccessionNumberGenerator : IImagingAccessionNumberGenerator
{
    public string Generate(string modality, DateTime utcDate)
    {
        return $"{modality}{utcDate:yyMMdd}TEST01";
    }
}

internal sealed class NoOpQueueDisplayHubContext : IHubContext<QueueDisplayHub>
{
    public IHubClients Clients { get; } = new RecordingHubClients();
    public IGroupManager Groups { get; } = new NoOpGroupManager();
}

internal sealed class AllowAllUserDepartmentFeatureAccessService
    : IUserDepartmentFeatureAccessService
{
    public Task<bool> IsFeatureEnabledAsync(Guid? departmentId, string featureKey)
        => Task.FromResult(true);

    public Task<bool> CanCurrentUserAccessFeatureAsync(string featureKey)
        => Task.FromResult(true);

    public Task<bool> CanUserAccessFeatureAsync(Guid tenantId, Guid userId, string featureKey)
        => Task.FromResult(true);

    public Task<EffectiveDepartmentFeaturesResponse> GetCurrentUserEffectiveFeaturesAsync()
        => throw new NotImplementedException();
}



