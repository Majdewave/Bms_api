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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

/// <summary>
/// Focused coverage for the Client.IdNumber accidental-clearing fix in ClientsController.Update.
/// An omitted/null/empty idNumber on a normal update must NOT erase an existing identifier.
/// </summary>
[TestClass]
public sealed class ClientsControllerIdNumberTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    private static async Task<(SqliteConnection, AppDbContext, TestTenantContext, Guid tenantId, Guid clientId)> NewFixtureAsync(string? initialIdNumber)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);
        var tenantContext = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        tenantContext.SetTenant(tenantId);
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Biz", Subdomain = "biz-" + Guid.NewGuid().ToString("N"), ProductType = TenantProductType.Business, Plan = PlanType.Pro });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, FullName = "Client", IdNumber = initialIdNumber, Phone = "123" });
        await db.SaveChangesAsync();
        return (connection, db, tenantContext, tenantId, clientId);
    }

    private static ClientsController CreateController(AppDbContext db, ITenantContext tenantContext) =>
        new(db, tenantContext) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [TestMethod]
    public async Task Update_NullIdNumber_PreservesExisting()
    {
        var (connection, db, tenantContext, tenantId, clientId) = await NewFixtureAsync("516264675");
        await using var _ = connection;
        var controller = CreateController(db, tenantContext);

        var result = await controller.Update(clientId, new UpdateClientRequest(
            FullName: "Renamed", IdNumber: null, BirthDate: null, Email: null, Phone: "999", Address: null, InternalNote: null, IsActive: true));

        Assert.IsInstanceOfType(result, typeof(NoContentResult));
        Assert.AreEqual("516264675", (await db.Clients.SingleAsync(c => c.Id == clientId)).IdNumber);
        Assert.AreEqual("999", (await db.Clients.SingleAsync(c => c.Id == clientId)).Phone);
    }

    [TestMethod]
    public async Task Update_EmptyIdNumber_PreservesExisting()
    {
        var (connection, db, tenantContext, tenantId, clientId) = await NewFixtureAsync("516264675");
        await using var _ = connection;
        var controller = CreateController(db, tenantContext);

        var result = await controller.Update(clientId, new UpdateClientRequest(
            FullName: "Renamed", IdNumber: "   ", BirthDate: null, Email: null, Phone: "999", Address: null, InternalNote: null, IsActive: true));

        Assert.IsInstanceOfType(result, typeof(NoContentResult));
        Assert.AreEqual("516264675", (await db.Clients.SingleAsync(c => c.Id == clientId)).IdNumber);
    }

    [TestMethod]
    public async Task Update_NewValidIdNumber_Updates()
    {
        var (connection, db, tenantContext, tenantId, clientId) = await NewFixtureAsync("516264675");
        await using var _ = connection;
        var controller = CreateController(db, tenantContext);

        var result = await controller.Update(clientId, new UpdateClientRequest(
            FullName: "Renamed", IdNumber: " 516264676 ", BirthDate: null, Email: null, Phone: "999", Address: null, InternalNote: null, IsActive: true));

        Assert.IsInstanceOfType(result, typeof(NoContentResult));
        Assert.AreEqual("516264676", (await db.Clients.SingleAsync(c => c.Id == clientId)).IdNumber);
    }

    [TestMethod]
    public async Task Update_OtherFieldsUpdate_WhenIdNumberOmitted()
    {
        var (connection, db, tenantContext, tenantId, clientId) = await NewFixtureAsync("516264675");
        await using var _ = connection;
        var controller = CreateController(db, tenantContext);

        var result = await controller.Update(clientId, new UpdateClientRequest(
            FullName: "New Name", IdNumber: null, BirthDate: null, Email: "a@b.co", Phone: "555", Address: "Street 1", InternalNote: "note", IsActive: false));

        Assert.IsInstanceOfType(result, typeof(NoContentResult));
        var client = await db.Clients.SingleAsync(c => c.Id == clientId);
        Assert.AreEqual("516264675", client.IdNumber); // preserved
        Assert.AreEqual("New Name", client.FullName);
        Assert.AreEqual("a@b.co", client.Email);
        Assert.AreEqual("555", client.Phone);
        Assert.AreEqual("Street 1", client.Address);
        Assert.AreEqual("note", client.InternalNote);
        Assert.IsFalse(client.IsActive);
    }
}
