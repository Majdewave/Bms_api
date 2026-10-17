using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class AuthSeedServiceEnvironmentTests
{
    [TestMethod]
    public async Task DevelopmentEnvironmentSeedsDemoAccountsAndPermissionReferences()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        await new AuthSeedService(fixture.Context, new TestHostEnvironment(Environments.Development)).SeedAsync();

        Assert.AreEqual(1, await fixture.Context.Tenants.CountAsync(tenant => tenant.Subdomain == "default"));
        Assert.AreEqual(1, await fixture.Context.Users.CountAsync(user => user.Email == "admin@clienta.local" && user.Role == "Admin"));
        Assert.AreEqual(1, await fixture.Context.Users.CountAsync(user => user.Email == "staff@clienta.local" && user.Role == "Staff"));
        Assert.IsTrue(await fixture.Context.Permissions.AnyAsync(permission => permission.Key == "manage_projects"));
        Assert.AreEqual(3, await fixture.Context.UserPermissions.CountAsync());
    }

    [TestMethod]
    public async Task ProductionEnvironmentKeepsPermissionSeedingButDoesNotCreateOrDeleteDemoData()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var productionTenant = new Tenant { Id = Guid.NewGuid(), Name = "Existing production tenant", Subdomain = "production-tenant" };
        var existingStaff = new User
        {
            Id = Guid.NewGuid(), Email = "existing-staff@example.invalid", PasswordHash = "test-hash",
            Role = "Staff", TenantId = productionTenant.Id, IsActive = true,
        };
        fixture.Context.Tenants.Add(productionTenant);
        fixture.Context.Users.Add(existingStaff);
        await fixture.Context.SaveChangesAsync();

        await new AuthSeedService(fixture.Context, new TestHostEnvironment(Environments.Production)).SeedAsync();

        Assert.AreEqual(0, await fixture.Context.Tenants.CountAsync(tenant => tenant.Subdomain == "default"));
        Assert.AreEqual(0, await fixture.Context.Users.CountAsync(user => user.Email == "admin@clienta.local"));
        Assert.AreEqual(0, await fixture.Context.Users.CountAsync(user => user.Email == "staff@clienta.local"));
        Assert.IsTrue(await fixture.Context.Users.AnyAsync(user => user.Id == existingStaff.Id));
        Assert.IsTrue(await fixture.Context.Permissions.AnyAsync(permission => permission.Key == "manage_projects"));
        Assert.AreEqual(3, await fixture.Context.UserPermissions.CountAsync(link => link.UserId == existingStaff.Id));
    }

    private sealed class SeedFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly SqliteConnection _connection;
        private readonly TestTenantContext _tenantContext = new();

        private SeedFixture(string databasePath)
        {
            _databasePath = databasePath;
            _connection = new SqliteConnection($"Data Source={databasePath}");
            _connection.Open();
            _connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
            Context = new AppDbContext(options, _tenantContext);
        }

        public AppDbContext Context { get; }

        public static async Task<SeedFixture> CreateAsync()
        {
            var fixture = new SeedFixture(Path.Combine(Path.GetTempPath(), $"clienta-auth-seed-{Guid.NewGuid():N}.db"));
            await fixture.Context.Database.EnsureCreatedAsync();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Clienta.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
