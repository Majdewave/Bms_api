using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class QueueConsistencyTests
{
    [TestMethod]
    public async Task AppointmentDate_Should_SynchronizeFromStartTime_When_AppointmentIsCreatedOrUpdated()
    {
        // Business rule: AppointmentDate is derived persistence data from StartTime.Date.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var controller = fixture.CreateController(context);

        var start = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        var created = await QueueTestHelpers.CreateScheduledAppointmentAsync(controller, fixture.Client.Id, start);

        var entity = await context.Appointments.FirstAsync(a => a.Id == created.Id);
        Assert.AreEqual(start.Date, entity.AppointmentDate.Date);

        var newStart = start.AddDays(1).AddHours(2);
        var update = await controller.Update(entity.Id, new UpdateAppointmentRequest(
            newStart,
            newStart.AddMinutes(30),
            entity.Status,
            entity.Notes,
            entity.ServiceId,
            entity.StaffId,
            entity.IsDocumented,
            entity.QueueNumber));

        Assert.IsInstanceOfType<OkObjectResult>(update);

        var refreshed = await context.Appointments.FirstAsync(a => a.Id == entity.Id);
        Assert.AreEqual(newStart.Date, refreshed.AppointmentDate.Date);
    }

    [TestMethod]
    public async Task SaveChanges_Should_SelfHealAppointmentDate_When_StoredValueIsInconsistent()
    {
        // Business rule: backend never trusts persisted AppointmentDate and auto-corrects mismatches.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();

        var start = new DateTime(2026, 7, 22, 9, 0, 0, DateTimeKind.Utc);
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            ClientId = fixture.Client.Id,
            CreatedByUserId = fixture.User.Id,
            StartTime = start,
            EndTime = start.AddMinutes(30),
            AppointmentDate = start.AddDays(10).Date,
            Status = AppointmentStatuses.Scheduled,
            CreatedAt = DateTime.UtcNow,
        };

        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        var loaded = await context.Appointments.FirstAsync(a => a.Id == appointment.Id);
        Assert.AreEqual(start.Date, loaded.AppointmentDate.Date);

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE \"Appointments\" SET \"AppointmentDate\" = {0} WHERE \"Id\" = {1}",
            start.AddDays(3).Date,
            appointment.Id);

        var controller = fixture.CreateController(context);
        var update = await controller.Update(appointment.Id, new UpdateAppointmentRequest(
            default,
            default,
            AppointmentStatuses.Waiting,
            null,
            null,
            null,
            null,
            null));

        Assert.IsInstanceOfType<OkObjectResult>(update);

        var corrected = await context.Appointments.FirstAsync(a => a.Id == appointment.Id);
        Assert.AreEqual(corrected.StartTime.Date, corrected.AppointmentDate.Date);
    }

    [TestMethod]
    public async Task SaveChanges_Should_EnforceUniqueQueueNumber_When_TenantDateAndWaitingMatch()
    {
        // Business rule: queue numbers must be unique per tenant/date inside waiting queue.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();

        var date = new DateTime(2026, 7, 25, 8, 0, 0, DateTimeKind.Utc);
        context.Appointments.AddRange(
            new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = fixture.Tenant.Id,
                ClientId = fixture.Client.Id,
                CreatedByUserId = fixture.User.Id,
                StartTime = date,
                EndTime = date.AddMinutes(30),
                AppointmentDate = date.Date,
                Status = AppointmentStatuses.Waiting,
                QueueNumber = 1,
                CreatedAt = DateTime.UtcNow,
            },
            new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = fixture.Tenant.Id,
                ClientId = fixture.Client.Id,
                CreatedByUserId = fixture.User.Id,
                StartTime = date.AddHours(1),
                EndTime = date.AddHours(1).AddMinutes(30),
                AppointmentDate = date.Date,
                Status = AppointmentStatuses.Waiting,
                QueueNumber = 1,
                CreatedAt = DateTime.UtcNow,
            });

        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [TestMethod]
    public async Task Reorder_Should_EndWithConsistentQueue_When_ConcurrentRequestsOccur()
    {
        // Business rule: concurrent reorder requests must not leave duplicate or missing queue numbers.
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var seedContext = fixture.CreateContext();
        var seedController = fixture.CreateController(seedContext);
        var appointments = await QueueTestHelpers.SeedWaitingQueueAsync(seedController, seedContext, fixture.Client.Id, 4);
        var ordered = appointments.OrderBy(a => a.QueueNumber).ToList();

        await using var contextOne = fixture.CreateContext();
        await using var contextTwo = fixture.CreateContext();
        var controllerOne = fixture.CreateController(contextOne);
        var controllerTwo = fixture.CreateController(contextTwo);

        var reqOne = new ReorderWaitingQueueRequest(new List<ReorderWaitingQueueItemRequest>
        {
            new(ordered[3].Id, 1), new(ordered[0].Id, 2), new(ordered[1].Id, 3), new(ordered[2].Id, 4)
        });

        var reqTwo = new ReorderWaitingQueueRequest(new List<ReorderWaitingQueueItemRequest>
        {
            new(ordered[1].Id, 1), new(ordered[2].Id, 2), new(ordered[3].Id, 3), new(ordered[0].Id, 4)
        });

        var results = await Task.WhenAll(
            controllerOne.ReorderWaitingQueue(reqOne),
            controllerTwo.ReorderWaitingQueue(reqTwo));

        foreach (var result in results)
        {
            Assert.IsTrue(result is NoContentResult or BadRequestObjectResult);
        }

        await using var verifyContext = fixture.CreateContext();
        var finalQueue = await verifyContext.Appointments
            .Where(a => a.TenantId == fixture.Tenant.Id && a.Status == AppointmentStatuses.Waiting)
            .OrderBy(a => a.QueueNumber)
            .ToListAsync();

        Assert.AreEqual(4, finalQueue.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, finalQueue.Select(a => a.QueueNumber).ToArray());
        Assert.AreEqual(4, finalQueue.Select(a => a.Id).Distinct().Count());
    }
}
