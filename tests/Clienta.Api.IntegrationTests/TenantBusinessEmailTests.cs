using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class TenantBusinessEmailTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private sealed class NoopFileStorage : IFileStorage
    {
        public Task<string> UploadAsync(Stream fileStream, string fileName, string contentType) =>
            Task.FromResult(fileName);

        public Task<Stream> OpenReadAsync(string fileKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string fileKey) => Task.CompletedTask;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(SqliteConnection connection, AppDbContext db, TenantController controller, Guid userId)
        {
            Connection = connection;
            Db = db;
            Controller = controller;
            UserId = userId;
        }

        public SqliteConnection Connection { get; }
        public AppDbContext Db { get; }
        public TenantController Controller { get; }
        public Guid UserId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);

            var tenantContext = new TestTenantContext { TenantId = Guid.NewGuid() };
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, tenantContext);
            await db.Database.EnsureCreatedAsync();

            var userId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = tenantContext.TenantId,
                Name = "Business",
                Subdomain = $"business-{Guid.NewGuid():N}",
                ProductType = TenantProductType.Business,
                Plan = PlanType.Pro
            });
            await db.SaveChangesAsync();

            db.Users.Add(new User
            {
                Id = userId,
                TenantId = tenantContext.TenantId,
                Email = "owner@example.com",
                PasswordHash = "test-hash",
                Role = "admin"
            });
            await db.SaveChangesAsync();

            var tenant = await db.Tenants.SingleAsync();
            tenant.OwnerUserId = userId;
            await db.SaveChangesAsync();

            var controller = new TenantController(
                db,
                tenantContext,
                new NoopFileStorage(),
                new TenantProductService(db));

            return new Fixture(connection, db, controller, userId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task GetMe_UsesOwnerEmailWhenBusinessEmailIsUnset()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = (OkObjectResult)await fixture.Controller.GetMe();

        Assert.AreEqual("owner@example.com", GetProperty<string>(result.Value!, "businessEmail"));
        Assert.AreEqual("owner@example.com", GetProperty<string>(result.Value!, "email"));
    }

    [TestMethod]
    public async Task UpdateMe_TrimsBusinessEmailWithoutChangingOwnerEmail()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Controller.UpdateMe(new UpdateTenantRequest
        {
            BusinessEmail = "  contact@example.com  "
        });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        Assert.AreEqual("contact@example.com", (await fixture.Db.Tenants.SingleAsync()).BusinessEmail);
        Assert.AreEqual("owner@example.com", (await fixture.Db.Users.SingleAsync()).Email);
    }

    [TestMethod]
    public async Task UpdateMe_BlankBusinessEmailClearsOverrideAndFallsBackToOwnerEmail()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Db.Tenants.SingleAsync();
        tenant.BusinessEmail = "contact@example.com";
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.UpdateMe(new UpdateTenantRequest { BusinessEmail = "  " });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        Assert.IsNull((await fixture.Db.Tenants.SingleAsync()).BusinessEmail);
        var getResult = (OkObjectResult)await fixture.Controller.GetMe();
        Assert.AreEqual("owner@example.com", GetProperty<string>(getResult.Value!, "businessEmail"));
    }

    [TestMethod]
    public async Task UpdateMe_RejectsInvalidBusinessEmailWithoutSaving()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Controller.UpdateMe(new UpdateTenantRequest
        {
            BusinessEmail = "not-an-email"
        });

        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        Assert.IsNull((await fixture.Db.Tenants.SingleAsync()).BusinessEmail);
        Assert.AreEqual("owner@example.com", (await fixture.Db.Users.SingleAsync()).Email);
    }

    private static T? GetProperty<T>(object value, string name) =>
        (T?)value.GetType().GetProperty(name)?.GetValue(value);
}
