using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class QueueReorderTests
{
    [TestMethod]
    public async Task Reorder_Should_KeepExistingQueueNumberRange_When_DragDropOccurs()
    {
        // Business rule: reorder swaps positions but keeps the existing active queue number range.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context);

        var appointments = await QueueTestHelpers.SeedWaitingQueueAsync(controller, context, fixture.Client.Id, 4);
        var currentOrder = appointments.OrderBy(a => a.QueueNumber).ToList();

        var movedLastToTop = new List<ReorderWaitingQueueItemRequest>
        {
            new(currentOrder[3].Id, 1),
            new(currentOrder[0].Id, 2),
            new(currentOrder[1].Id, 3),
            new(currentOrder[2].Id, 4)
        };

        var reorder = await controller.ReorderWaitingQueue(new ReorderWaitingQueueRequest(movedLastToTop));
        Assert.IsInstanceOfType<NoContentResult>(reorder);

        var refreshed = await context.Appointments
            .Where(a => a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, refreshed.Select(a => a.QueueNumber).ToArray());
        Assert.AreEqual(currentOrder[3].Id, refreshed[0].Id);
    }

    [TestMethod]
    public async Task Reorder_Should_RejectPayload_When_QueueNumberRangeChanges()
    {
        // Business rule: reorder payload must preserve existing queue number values for the active date queue.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context);

        var appointments = await QueueTestHelpers.SeedWaitingQueueAsync(controller, context, fixture.Client.Id, 3);
        var sorted = appointments.OrderBy(a => a.QueueNumber).ToList();

        var invalid = new ReorderWaitingQueueRequest(new List<ReorderWaitingQueueItemRequest>
        {
            new(sorted[0].Id, 2),
            new(sorted[1].Id, 3),
            new(sorted[2].Id, 4)
        });

        var response = await controller.ReorderWaitingQueue(invalid);
        Assert.IsInstanceOfType<BadRequestObjectResult>(response);
    }

    [TestMethod]
    public async Task Reorder_Should_NotRestartFromOne_When_CompletedLeavesGapAtFront()
    {
        // Business rule: if queue 1 is no longer active, reorder keeps remaining range (e.g., 2..N).
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context);

        var appointments = await QueueTestHelpers.SeedWaitingQueueAsync(controller, context, fixture.Client.Id, 3);
        var ordered = appointments.OrderBy(a => a.QueueNumber).ToList();

        // Move the first active slot out of active queue to simulate completion of queue #1.
        var complete = await controller.Update(ordered[0].Id, new UpdateAppointmentRequest(
            default,
            default,
            AppointmentStatuses.InProgress,
            null,
            null,
            null,
            null,
            null));
        Assert.IsInstanceOfType<OkObjectResult>(complete);

        complete = await controller.Update(ordered[0].Id, new UpdateAppointmentRequest(
            default,
            default,
            AppointmentStatuses.Completed,
            null,
            null,
            null,
            null,
            null));
        Assert.IsInstanceOfType<OkObjectResult>(complete);

        var active = await context.Appointments
            .Where(a => a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { 2, 3 }, active.Select(a => a.QueueNumber).ToArray());

        var reorder = await controller.ReorderWaitingQueue(new ReorderWaitingQueueRequest(new List<ReorderWaitingQueueItemRequest>
        {
            new(active[1].Id, 2),
            new(active[0].Id, 3)
        }));
        Assert.IsInstanceOfType<NoContentResult>(reorder);

        var refreshed = await context.Appointments
            .Where(a => a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { 2, 3 }, refreshed.Select(a => a.QueueNumber).ToArray());
        Assert.AreEqual(active[1].Id, refreshed[0].Id);
    }

    [TestMethod]
    public async Task Reorder_Should_PreserveExistingOrder_When_BulkValidationFails()
    {
        // Business rule: bulk reorder is all-or-nothing and must not leave partial updates.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context);

        var appointments = await QueueTestHelpers.SeedWaitingQueueAsync(controller, context, fixture.Client.Id, 3);
        var before = appointments.OrderBy(a => a.QueueNumber).Select(a => (a.Id, a.QueueNumber)).ToList();

        var invalid = new ReorderWaitingQueueRequest(new List<ReorderWaitingQueueItemRequest>
        {
            new(before[0].Id, 1),
            new(before[1].Id, 1),
            new(before[2].Id, 2)
        });

        var response = await controller.ReorderWaitingQueue(invalid);
        Assert.IsInstanceOfType<BadRequestObjectResult>(response);

        var after = await context.Appointments
            .Where(a => a.TenantId == fixture.Tenant.Id && a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber)
            .Select(a => new { a.Id, a.QueueNumber })
            .ToListAsync();

        CollectionAssert.AreEqual(before.Select(x => x.QueueNumber).ToArray(), after.Select(x => x.QueueNumber).ToArray());
        CollectionAssert.AreEqual(before.Select(x => x.Id).ToArray(), after.Select(x => x.Id).ToArray());
    }
}
