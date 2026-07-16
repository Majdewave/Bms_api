using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class QueueLifecycleTests
{
    [TestMethod]
    public async Task Create_Should_AssignQueueNumber_When_AppointmentEntersWaiting()
    {
        // Business rule: entering Waiting gets the next queue number for the day.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context);

        var createdOne = await QueueTestHelpers.CreateScheduledAppointmentAsync(controller, fixture.Client.Id, DateTime.UtcNow.AddHours(1));
        var createdTwo = await QueueTestHelpers.CreateScheduledAppointmentAsync(controller, fixture.Client.Id, DateTime.UtcNow.AddHours(2));

        await QueueTestHelpers.MoveToWaitingAsync(controller, context, createdOne.Id);
        await QueueTestHelpers.MoveToWaitingAsync(controller, context, createdTwo.Id);

        var waiting = await context.Appointments
            .Where(a => a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { 1, 2 }, waiting.Select(a => a.QueueNumber).ToArray());
    }

    [TestMethod]
    public async Task Update_Should_KeepRemainingQueueNumbers_When_WaitingMovesToInProgress()
    {
        // Business rule: starting treatment must not renumber remaining waiting appointments.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context);

        var appointments = await QueueTestHelpers.SeedWaitingQueueAsync(controller, context, fixture.Client.Id, 3);

        var first = appointments.OrderBy(a => a.QueueNumber).First();
        var update = await controller.Update(first.Id, new UpdateAppointmentRequest(
            default,
            default,
            AppointmentStatuses.InProgress,
            null,
            null,
            null,
            null,
            null));

        Assert.IsInstanceOfType<OkObjectResult>(update);

        var remaining = await context.Appointments
            .Where(a => a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { 2, 3 }, remaining.Select(a => a.QueueNumber).ToArray());
    }

    [TestMethod]
    public async Task Delete_Should_KeepRemainingQueueNumbers_When_WaitingAppointmentIsRemoved()
    {
        // Business rule: deleting from waiting should not compact queue numbers automatically.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context);

        var appointments = await QueueTestHelpers.SeedWaitingQueueAsync(controller, context, fixture.Client.Id, 3);
        var first = appointments.OrderBy(a => a.QueueNumber).First();

        var delete = await controller.Delete(first.Id);
        Assert.IsInstanceOfType<NoContentResult>(delete);

        var remaining = await context.Appointments
            .Where(a => a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { 2, 3 }, remaining.Select(a => a.QueueNumber).ToArray());
    }

    [TestMethod]
    public async Task Queue_Should_PersistQueueNumbers_When_DataIsReloadedFromDatabase()
    {
        // Business rule: queue numbers are persisted data, not UI-calculated positions.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context);

        await QueueTestHelpers.SeedWaitingQueueAsync(controller, context, fixture.Client.Id, 3);

        await using var reloadContext = fixture.CreateContext();
        var reloaded = await reloadContext.Appointments
            .Where(a => a.TenantId == fixture.Tenant.Id && a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber)
            .Select(a => a.QueueNumber)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, reloaded.ToArray());
    }
}
