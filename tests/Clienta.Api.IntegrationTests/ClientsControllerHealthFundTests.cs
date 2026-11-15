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

[TestClass]
public sealed class ClientsControllerHealthFundTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public Guid? UserId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void SetUserId(Guid userId) => UserId = userId;
    }

    [TestMethod]
    public async Task CreateAndUpdate_PersistHealthFundAndReturnItInClientList()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);

        var tenantContext = new TestTenantContext();
        var tenantId = Guid.NewGuid();
        tenantContext.SetTenant(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Health Fund test",
            Subdomain = $"health-fund-{Guid.NewGuid():N}",
            ProductType = TenantProductType.Clinic,
            Plan = PlanType.Pro
        });
        await db.SaveChangesAsync();

        var controller = new ClientsController(db, tenantContext)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var createResult = await controller.Create(new CreateClientRequest(
            "Client", null, null, null, "123", null, null, "Other", "Regional fund"));
        Assert.IsInstanceOfType(createResult, typeof(CreatedAtActionResult));

        var client = await db.Clients.SingleAsync();
        Assert.AreEqual("Other", client.HealthFund);
        Assert.AreEqual("Regional fund", client.HealthFundOther);

        var listResult = await controller.GetAll();
        Assert.IsInstanceOfType(listResult, typeof(OkObjectResult));
        var listedClient = ((IEnumerable<ClientResponse>)((OkObjectResult)listResult).Value!).Single();
        Assert.AreEqual("Other", listedClient.HealthFund);
        Assert.AreEqual("Regional fund", listedClient.HealthFundOther);

        var updateResult = await controller.Update(client.Id, new UpdateClientRequest(
            "Client", null, null, null, "123", null, null, true, "Clalit", "Ignored"));
        Assert.IsInstanceOfType(updateResult, typeof(NoContentResult));
        client = await db.Clients.SingleAsync();
        Assert.AreEqual("Clalit", client.HealthFund);
        Assert.IsNull(client.HealthFundOther);

        await controller.Update(client.Id, new UpdateClientRequest(
            "Client", null, null, null, "123", null, null, true));
        client = await db.Clients.SingleAsync();
        Assert.AreEqual("Clalit", client.HealthFund);

        await controller.Update(client.Id, new UpdateClientRequest(
            "Client", null, null, null, "123", null, null, true, "", ""));
        client = await db.Clients.SingleAsync();
        Assert.IsNull(client.HealthFund);
        Assert.IsNull(client.HealthFundOther);
    }

    [TestMethod]
    public async Task Create_RejectsHealthFundOutsideStableCodes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);

        var tenantContext = new TestTenantContext();
        tenantContext.SetTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        var controller = new ClientsController(db, tenantContext)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Create(new CreateClientRequest(
            "Client", null, null, null, "123", null, null, "clalit", null));

        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        Assert.AreEqual(0, await db.Clients.CountAsync());
    }
}
