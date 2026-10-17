using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Clienta.Api.Services.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Runtime.Serialization;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class PlatformBusinessLifecycleTests
{
    [TestMethod]
    public async Task Suspend_Should_Block_Login_And_TenantAccess()
    {
        await using var fixture = await PlatformLifecycleTestFixture.CreateAsync();
        await using var app = fixture.CreateAppContext();
        await using var master = fixture.CreateMasterContext();
        var tenantApprovalService = fixture.CreateTenantApprovalService(app, master);
        var controller = fixture.CreateAuthController(app, tenantApprovalService);
        var validator = new TenantAccessValidator(master);

        var loginBefore = await PlatformLifecycleTestFixture.InvokeLoginAsync(controller, fixture.OwnerEmail, fixture.TenantPassword);
        Assert.IsInstanceOfType<OkObjectResult>(loginBefore);

        var service = fixture.CreatePlatformTenantService(master, app, tenantApprovalService);
        await service.SuspendTenantAsync(fixture.Tenant.Id, CancellationToken.None);

        var loginAfter = await PlatformLifecycleTestFixture.InvokeLoginAsync(controller, fixture.OwnerEmail, fixture.TenantPassword);
        var forbidden = loginAfter as ObjectResult;
        Assert.IsNotNull(forbidden);
        Assert.AreEqual(StatusCodes.Status403Forbidden, forbidden!.StatusCode);

        var allowed = await validator.IsTenantAllowedAsync(fixture.Tenant.Id, CancellationToken.None);
        Assert.IsFalse(allowed);
    }

    [TestMethod]
    public async Task Activate_Should_Restore_Login_Access()
    {
        await using var fixture = await PlatformLifecycleTestFixture.CreateAsync();
        await using var app = fixture.CreateAppContext();
        await using var master = fixture.CreateMasterContext();
        var validator = new TenantAccessValidator(master);
        var service = fixture.CreatePlatformTenantService(master, app, fixture.CreateTenantApprovalService(app, master));

        await service.SuspendTenantAsync(fixture.Tenant.Id, CancellationToken.None);
        Assert.IsFalse(await validator.IsTenantAllowedAsync(fixture.Tenant.Id, CancellationToken.None));

        await service.ActivateTenantAsync(fixture.Tenant.Id, CancellationToken.None);
        Assert.IsTrue(await validator.IsTenantAllowedAsync(fixture.Tenant.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task ExtendTrial_Should_Move_TrialEndsAt_And_Keep_TrialStartsAt()
    {
        await using var fixture = await PlatformLifecycleTestFixture.CreateAsync();
        await using var app = fixture.CreateAppContext();
        await using var master = fixture.CreateMasterContext();
        var service = fixture.CreatePlatformTenantService(master, app, fixture.CreateTenantApprovalService(app, master));

        var before = await master.Tenants.FirstAsync(t => t.Id == fixture.TrialTenant.Id);
        var originalStart = before.TrialStartsAt;
        var originalEnd = before.TrialEndsAt;

        await service.ExtendTrialAsync(fixture.TrialTenant.Id, 14, CancellationToken.None);

        var after = await master.Tenants.FirstAsync(t => t.Id == fixture.TrialTenant.Id);
        Assert.AreEqual(originalStart, after.TrialStartsAt);
        Assert.IsNotNull(originalEnd);
        Assert.IsNotNull(after.TrialEndsAt);
        Assert.AreEqual(originalEnd!.Value.AddDays(14), after.TrialEndsAt);
    }

    [TestMethod]
    public async Task Approve_Should_Transition_Pending_Business_To_Trialing()
    {
        await using var fixture = await PlatformLifecycleTestFixture.CreateAsync();
        await using var app = fixture.CreateAppContext();
        await using var master = fixture.CreateMasterContext();
        var approvalService = fixture.CreateTenantApprovalService(app, master);
        var service = fixture.CreatePlatformTenantService(master, app, approvalService);

        await service.ApproveTenantAsync(fixture.PendingTenant.Id, CancellationToken.None);

        await using var verificationMaster = fixture.CreateMasterContext();
        var approved = await verificationMaster.Tenants.FirstAsync(t => t.Id == fixture.PendingTenant.Id);
        Assert.AreEqual(SubscriptionStatus.Trialing, approved.SubscriptionStatus);
        Assert.IsTrue(approved.IsTrial);
        Assert.IsNotNull(approved.TrialStartsAt);
        Assert.IsNotNull(approved.TrialEndsAt);
        Assert.AreEqual(7, (approved.TrialEndsAt!.Value - approved.TrialStartsAt!.Value).TotalDays, 0.01);
    }

    [TestMethod]
    public async Task Approve_Clinic_UsesClinicTrialDays()
    {
        await using var fixture = await PlatformLifecycleTestFixture.CreateAsync();
        await using var app = fixture.CreateAppContext();
        await using var master = fixture.CreateMasterContext();
        var pending = await app.Tenants.FirstAsync(tenant => tenant.Id == fixture.PendingTenant.Id);
        pending.ProductType = TenantProductType.Clinic;
        await app.SaveChangesAsync();

        var approval = fixture.CreateTenantApprovalService(app, master);
        await approval.ApproveTenantAsync(pending.Id, CancellationToken.None);

        var approved = await master.Tenants.FirstAsync(tenant => tenant.Id == pending.Id);
        Assert.AreEqual(21, (approved.TrialEndsAt!.Value - approved.TrialStartsAt!.Value).TotalDays, 0.01);
    }

    [TestMethod]
    public async Task Approve_Business_UsesBusinessTrialDays()
    {
        await using var fixture = await PlatformLifecycleTestFixture.CreateAsync();
        await using var app = fixture.CreateAppContext();
        await using var master = fixture.CreateMasterContext();
        var pending = await app.Tenants.FirstAsync(tenant => tenant.Id == fixture.PendingTenant.Id);
        pending.ProductType = TenantProductType.Business;
        await app.SaveChangesAsync();

        var approval = fixture.CreateTenantApprovalService(app, master);
        await approval.ApproveTenantAsync(pending.Id, CancellationToken.None);

        var approved = await master.Tenants.FirstAsync(tenant => tenant.Id == pending.Id);
        Assert.AreEqual(14, (approved.TrialEndsAt!.Value - approved.TrialStartsAt!.Value).TotalDays, 0.01);
    }

    [TestMethod]
    public async Task PlatformTrialSettings_UpdateIndependentlyWithoutRewritingExistingTenantEndDate()
    {
        await using var fixture = await PlatformLifecycleTestFixture.CreateAsync();
        await using var master = fixture.CreateMasterContext();
        var tenant = await master.Tenants.FirstAsync(item => item.Id == fixture.TrialTenant.Id);
        var originalTrialEnd = tenant.TrialEndsAt;
        var settingsService = new PlatformSettingsService(master);

        var updated = await settingsService.UpdateAsync(new Clienta.Api.DTOs.UpdatePlatformSettingsRequest(
            SupportEmail: "support@test.local",
            SupportPhone: null,
            WebsiteUrl: "https://example.invalid",
            DefaultTrialDays: 7,
            ClinicTrialDays: 24,
            BusinessTrialDays: 12,
            TrialReminderDays: 3,
            AllowRegistrations: true,
            RequireManualApproval: true,
            EnableBilling: true,
            EnableHelpCenter: true,
            WhatsAppEnabled: false,
            ProMonthlyPrice: 46,
            ProAnnualPrice: 460,
            ProDescription: "Pro",
            ProEnabled: true,
            ProDisplayOrder: 1), CancellationToken.None);

        Assert.AreEqual(7, updated.DefaultTrialDays);
        Assert.AreEqual(24, updated.ClinicTrialDays);
        Assert.AreEqual(12, updated.BusinessTrialDays);
        await master.Entry(tenant).ReloadAsync();
        Assert.AreEqual(originalTrialEnd, tenant.TrialEndsAt);
    }

    [TestMethod]
    public void TrialDurationResolver_UnknownProductFallsBackToLegacyDefault()
    {
        var settings = new Clienta.Api.DTOs.PlatformSettingsDto(
            Guid.NewGuid(), "support@test.local", null, null,
            DefaultTrialDays: 7, ClinicTrialDays: 21, BusinessTrialDays: 14,
            TrialReminderDays: 3, AllowRegistrations: true, RequireManualApproval: true,
            EnableBilling: true, EnableHelpCenter: true, WhatsAppEnabled: false,
            ProMonthlyPrice: 46, ProAnnualPrice: 460, ProDescription: "Pro",
            ProEnabled: true, ProDisplayOrder: 1, UpdatedAt: DateTime.UtcNow);

        Assert.AreEqual(7, TrialDurationResolver.ResolveDays(null, settings));
        Assert.AreEqual(7, TrialDurationResolver.ResolveDays((TenantProductType)999, settings));
        Assert.AreEqual(21, TrialDurationResolver.ResolveDays(TenantProductType.Clinic, settings));
        Assert.AreEqual(14, TrialDurationResolver.ResolveDays(TenantProductType.Business, settings));
    }
}

internal sealed class PlatformLifecycleTestFixture : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly List<SqliteConnection> _connections = new();

    private PlatformLifecycleTestFixture(string databasePath)
    {
        _databasePath = databasePath;
        _connectionString = $"Data Source={databasePath}";
    }

    public Tenant Tenant { get; private set; } = null!;
    public Tenant TrialTenant { get; private set; } = null!;
    public Tenant PendingTenant { get; private set; } = null!;
    public string OwnerEmail { get; private set; } = string.Empty;
    public string TrialOwnerEmail { get; private set; } = string.Empty;
    public string PendingOwnerEmail { get; private set; } = string.Empty;
    public string TenantPassword => "Password123!";

    public static async Task<PlatformLifecycleTestFixture> CreateAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"clienta-platform-lifecycle-tests-{Guid.NewGuid():N}.db");
        var fixture = new PlatformLifecycleTestFixture(databasePath);
        await fixture.SeedAsync();
        return fixture;
    }

    public AppDbContext CreateAppContext()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
        _connections.Add(connection);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        return new AppDbContext(options, new TestTenantContext());
    }

    public MasterDbContext CreateMasterContext()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
        _connections.Add(connection);
        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseSqlite(connection)
            .Options;

        return new MasterDbContext(options);
    }

    public AuthController CreateAuthController(AppDbContext appDb, ITenantApprovalService tenantApprovalService)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Key"] = "0123456789abcdef0123456789abcdef",
                ["JwtSettings:Issuer"] = "clienta",
                ["JwtSettings:Audience"] = "clienta",
                ["JwtSettings:ExpiresInMinutes"] = "60",
                ["App:BaseUrl"] = "http://localhost",
                ["Support:Email"] = "support@test.local",
                ["Onboarding:ApprovalApiKey"] = "test-key"
            })
            .Build();

        return new AuthController(
            appDb,
            new JwtService(config, NullLogger<JwtService>.Instance, appDb),
            new NoOpAuthService(),
            NullLogger<AuthController>.Instance,
            new ResetRateLimiter(new MemoryCache(new MemoryCacheOptions())),
            new NoOpEmailService(),
            new OnboardingLocalizationService(config),
            tenantApprovalService,
            config);
    }

    public PlatformTenantManagementService CreatePlatformTenantService(
        MasterDbContext masterDb,
        AppDbContext appDb,
        ITenantApprovalService tenantApprovalService)
    {
        return new PlatformTenantManagementService(masterDb, appDb, tenantApprovalService);
    }

    public TenantApprovalService CreateTenantApprovalService(AppDbContext appDb, MasterDbContext masterDb)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Onboarding:TrialDays"] = "7",
                ["App:BaseUrl"] = "http://localhost",
                ["Support:Email"] = "support@test.local"
            })
            .Build();

        return new TenantApprovalService(
            appDb,
            new NoOpEmailService(),
            new OnboardingLocalizationService(config),
            config,
            new PlatformSettingsService(masterDb));
    }

    private async Task SeedAsync()
    {
        await using var app = CreateAppContext();
        await app.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        app.PlatformSettings.Add(new PlatformSettings
        {
            Id = Guid.NewGuid(),
            DefaultTrialDays = 7,
            ClinicTrialDays = 21,
            BusinessTrialDays = 14,
        });

        app.PlatformSettings.Add(new PlatformSettings
        {
            Id = Guid.NewGuid(),
            DefaultTrialDays = 7,
            ClinicTrialDays = 21,
            BusinessTrialDays = 14,
        });

        Tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Lifecycle Tenant",
            Subdomain = $"lifecycle-{Guid.NewGuid():N}",
            Plan = PlanType.Pro,
            SubscriptionStatus = SubscriptionStatus.Active,
            IsSuspended = false,
            CreatedAt = now.AddDays(-10),
            TrialStartsAt = now.AddDays(-3),
            TrialEndsAt = now.AddDays(4),
        };

        TrialTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Trial Tenant",
            Subdomain = $"trial-{Guid.NewGuid():N}",
            Plan = PlanType.Pro,
            SubscriptionStatus = SubscriptionStatus.Trialing,
            IsSuspended = false,
            CreatedAt = now.AddDays(-7),
            TrialStartsAt = now.AddDays(-2),
            TrialEndsAt = now.AddDays(5),
        };

        PendingTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Pending Tenant",
            Subdomain = $"pending-{Guid.NewGuid():N}",
            Plan = PlanType.Trial,
            SubscriptionStatus = SubscriptionStatus.PendingApproval,
            IsSuspended = false,
            CreatedAt = now.AddDays(-1),
        };

        app.Tenants.AddRange(Tenant, TrialTenant, PendingTenant);
        await app.SaveChangesAsync();

        var users = new[]
        {
            CreateOwnerUser(Tenant.Id, TenantPassword, "lifecycle-owner@test.local"),
            CreateOwnerUser(TrialTenant.Id, TenantPassword, "trial-owner@test.local"),
            CreateOwnerUser(PendingTenant.Id, TenantPassword, "pending-owner@test.local"),
        };

        app.Users.AddRange(users);
        await app.SaveChangesAsync();

        Tenant.OwnerUserId = users[0].Id;
        TrialTenant.OwnerUserId = users[1].Id;
        PendingTenant.OwnerUserId = users[2].Id;
        OwnerEmail = users[0].Email;
        TrialOwnerEmail = users[1].Email;
        PendingOwnerEmail = users[2].Email;
        await app.SaveChangesAsync();
    }

    private static User CreateOwnerUser(Guid tenantId, string password, string email)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = "Owner",
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow.AddDays(-20),
            IsActive = true,
            FullName = "Owner"
        };
    }

    public ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            connection.Dispose();
        }
        SqliteConnection.ClearAllPools();

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

    public static async Task<IActionResult> InvokeLoginAsync(AuthController controller, string email, string password)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var loginMethod = typeof(AuthController).GetMethod(nameof(AuthController.Login), BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(loginMethod);

        var requestType = loginMethod!.GetParameters()[0].ParameterType;
        object? request = null;

        foreach (var constructor in requestType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == 3)
            {
                request = constructor.Invoke(new object?[] { email, password, false });
                break;
            }

            if (parameters.Length == 0 && request == null)
            {
                request = constructor.Invoke(Array.Empty<object>());
            }
        }

        request ??= requestType.IsValueType
            ? Activator.CreateInstance(requestType)
            : FormatterServices.GetUninitializedObject(requestType);

        SetMemberValue(requestType, request, "Email", email);
        SetMemberValue(requestType, request, "Password", password);
        SetMemberValue(requestType, request, "RememberMe", false);

        Assert.IsNotNull(request);

        var result = loginMethod.Invoke(controller, new[] { request });
        Assert.IsNotNull(result);

        return await (Task<IActionResult>)result!;
    }

    private static void SetMemberValue(Type requestType, object request, string memberName, object? value)
    {
        var property = requestType.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null)
        {
            property.SetValue(request, value);
            return;
        }

        var field = requestType.GetField($"<{memberName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? requestType.GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (field != null)
        {
            field.SetValue(request, value);
        }
    }
}

internal sealed class NoOpEmailService : IEmailService
{
    public Task SendInviteEmailAsync(string email, string inviteLink) => Task.CompletedTask;
    public Task SendPasswordResetEmailAsync(string email, string resetLink) => Task.CompletedTask;
    public Task SendEmailAsync(string email, string subject, string body) => Task.CompletedTask;
    public Task SendTrialReminderAsync(string email, string companyName, string subdomain, int daysLeft, Guid tenantId) => Task.CompletedTask;
    public Task SendTrialExpiredAsync(string email, string companyName, string subdomain, Guid tenantId) => Task.CompletedTask;
}

internal sealed class NoOpAuthService : IAuthService
{
    public Task<bool> InviteUserAsync(string email, Guid tenantId) => Task.FromResult(true);

    public Task<bool> SendInviteToExistingUserAsync(Guid userId, Guid tenantId) => Task.FromResult(true);

    public Task<bool> AcceptInviteAsync(string token, string password, string? fullName = null) => Task.FromResult(true);

    public Task<bool> RequestPasswordResetAsync(string email) => Task.FromResult(true);

    public Task<bool> ResetPasswordAsync(string token, string newPassword) => Task.FromResult(true);
}
