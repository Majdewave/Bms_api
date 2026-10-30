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
public class ProjectPaymentTypeTests
{
    [TestMethod]
    public void ProjectPaymentTypeHasOnlyTheSupportedValues()
    {
        CollectionAssert.AreEqual(
            new[] { "Retainer", "Monthly", "Hourly", "Milestone" },
            Enum.GetNames<ProjectPaymentType>());
    }

    [TestMethod]
    public void ProjectPaymentTypeIsNullableAndDefaultsToNull()
    {
        Assert.IsNull(new Project().PaymentType);
        Assert.IsNull(new CreateProjectRequest(Guid.NewGuid(), "Project", ProjectStatus.Draft, 0, null, null, null).PaymentType);
        Assert.IsNull(new UpdateProjectRequest("Project", ProjectStatus.Draft, 0, null, null, null).PaymentType);
    }

    [TestMethod]
    public async Task CreateRejectsUndefinedProjectPaymentType()
    {
        var result = await CreateController().Create(new CreateProjectRequest(
            Guid.NewGuid(), "Project", ProjectStatus.Draft, 0, null, null, null, (ProjectPaymentType)99));

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    [TestMethod]
    public async Task UpdateRejectsUndefinedProjectPaymentType()
    {
        var result = await CreateController().Update(Guid.NewGuid(), new UpdateProjectRequest(
            "Project", ProjectStatus.Draft, 0, null, null, null, (ProjectPaymentType)99));

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    private static ProjectsController CreateController()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnection("Data Source=:memory:"))
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.NewGuid());

        return new ProjectsController(new AppDbContext(options, tenantContext), tenantContext, new EnabledPmAccessService());
    }

    private sealed class EnabledPmAccessService : IPmAccessService
    {
        public Task<bool> IsPmEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}