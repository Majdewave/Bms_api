using Clienta.Api.Controllers;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class AppointmentsTechnicianTests
{
    [TestMethod]
    public async Task TechnicianOptionsOnlyIncludeActiveSameTenantTechnicians()
    {
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var activeTechnician = CreateUser(fixture.Tenant.Id, "Technician", true);
        var inactiveTechnician = CreateUser(fixture.Tenant.Id, "Technician", false);
        var interpreter = CreateUser(fixture.Tenant.Id, "Interpreter", true);
        var generalStaff = CreateUser(fixture.Tenant.Id, "Staff", true);
        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Other Tenant",
            Subdomain = $"other-{Guid.NewGuid():N}",
        };
        var otherTenantTechnician = CreateUser(otherTenant.Id, "Technician", true);
        context.Tenants.Add(otherTenant);
        context.Users.AddRange(activeTechnician, inactiveTechnician, interpreter, generalStaff, otherTenantTechnician);
        context.BusinessUsers.AddRange(
            CreateBusinessUser(activeTechnician),
            CreateBusinessUser(inactiveTechnician),
            CreateBusinessUser(interpreter),
            CreateBusinessUser(generalStaff),
            CreateBusinessUser(otherTenantTechnician));
        await context.SaveChangesAsync();

        var result = await CreateController(fixture, context).GetTechnicianOptions(CancellationToken.None);

        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        var technicians = (IReadOnlyList<AppointmentTechnicianOptionResponse>)((OkObjectResult)result).Value!;
        Assert.AreEqual(1, technicians.Count);
        Assert.AreEqual(activeTechnician.Id, technicians[0].Id);
    }

    [TestMethod]
    public async Task ImagingAppointmentSynchronizesTechnicianAndAppointmentStaffAssignments()
    {
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var technician = CreateUser(fixture.Tenant.Id, "Technician", true);
        var replacementTechnician = CreateUser(fixture.Tenant.Id, "Technician", true);
        var interpreter = CreateUser(fixture.Tenant.Id, "Interpreter", true);
        var inactiveTechnician = CreateUser(fixture.Tenant.Id, "Technician", false);
        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Other Tenant",
            Subdomain = $"other-{Guid.NewGuid():N}",
        };
        var otherTenantTechnician = CreateUser(otherTenant.Id, "Technician", true);
        var technicianBusinessUser = CreateBusinessUser(technician);
        var replacementBusinessUser = CreateBusinessUser(replacementTechnician);
        var service = CreateService(fixture.Tenant.Id, "Ultrasound", "US");
        context.Tenants.Add(otherTenant);
        context.Users.AddRange(technician, replacementTechnician, interpreter, inactiveTechnician, otherTenantTechnician);
        context.BusinessUsers.AddRange(
            technicianBusinessUser,
            replacementBusinessUser,
            CreateBusinessUser(interpreter),
            CreateBusinessUser(inactiveTechnician),
            CreateBusinessUser(otherTenantTechnician));
        context.Services.Add(service);
        await context.SaveChangesAsync();

        var controller = CreateController(fixture, context);
        var startTime = new DateTime(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);

        var missingAssignment = await controller.Create(new CreateAppointmentRequest(
            fixture.Client.Id,
            startTime,
            startTime.AddMinutes(30),
            null,
            service.Id,
            null));
        Assert.IsInstanceOfType(missingAssignment, typeof(BadRequestObjectResult));

        foreach (var invalidTechnicianId in new[] { interpreter.Id, inactiveTechnician.Id, otherTenantTechnician.Id })
        {
            var invalidAssignment = await controller.Create(new CreateAppointmentRequest(
                fixture.Client.Id,
                startTime,
                startTime.AddMinutes(30),
                null,
                service.Id,
                null,
                PerformingTechnicianId: invalidTechnicianId));
            Assert.IsInstanceOfType(invalidAssignment, typeof(BadRequestObjectResult));
        }

        var createdResult = await controller.Create(new CreateAppointmentRequest(
            fixture.Client.Id,
            startTime,
            startTime.AddMinutes(30),
            null,
            service.Id,
            null,
            PerformingTechnicianId: technician.Id));
        Assert.IsInstanceOfType(createdResult, typeof(CreatedAtActionResult));
        var created = (AppointmentDto)((CreatedAtActionResult)createdResult).Value!;
        Assert.AreEqual(technician.Id, created.PerformingTechnicianId);
        Assert.AreEqual(technicianBusinessUser.Id, created.StaffId);

        var order = await context.ImagingOrders.SingleAsync();
        var appointment = await context.Appointments.SingleAsync();
        Assert.AreEqual(technician.Id, order.PerformingTechnicianId);
        Assert.AreEqual(technicianBusinessUser.Id, appointment.StaffId);
        Assert.AreEqual(technician.Id, (await context.BusinessUsers.SingleAsync(b => b.Id == appointment.StaffId)).UserId);

        var unrelatedUpdate = await controller.Update(
            created.Id,
            new UpdateAppointmentRequest(
                default,
                default,
                AppointmentStatuses.Scheduled,
                "Updated note",
                service.Id,
                null));
        Assert.IsInstanceOfType(unrelatedUpdate, typeof(OkObjectResult));
        Assert.AreEqual(technician.Id, order.PerformingTechnicianId);
        Assert.AreEqual(technicianBusinessUser.Id, appointment.StaffId);

        var replacementUpdate = await controller.Update(
            created.Id,
            new UpdateAppointmentRequest(
                default,
                default,
                AppointmentStatuses.Scheduled,
                null,
                service.Id,
                null,
                PerformingTechnicianId: replacementTechnician.Id));
        Assert.IsInstanceOfType(replacementUpdate, typeof(OkObjectResult));
        Assert.AreEqual(replacementTechnician.Id, order.PerformingTechnicianId);
        Assert.AreEqual(replacementBusinessUser.Id, appointment.StaffId);
    }

    [TestMethod]
    public async Task SwitchingNonImagingAppointmentToImagingCreatesOrderAndSynchronizesStaff()
    {
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var staff = CreateUser(fixture.Tenant.Id, "Staff", true);
        var technician = CreateUser(fixture.Tenant.Id, "Technician", true);
        var staffBusinessUser = CreateBusinessUser(staff);
        var technicianBusinessUser = CreateBusinessUser(technician);
        var regularService = CreateService(fixture.Tenant.Id, "Consultation", null);
        var imagingService = CreateService(fixture.Tenant.Id, "X-Ray", "DX");
        context.Users.AddRange(staff, technician);
        context.BusinessUsers.AddRange(staffBusinessUser, technicianBusinessUser);
        context.Services.AddRange(regularService, imagingService);
        await context.SaveChangesAsync();

        var controller = CreateController(fixture, context);
        var startTime = new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);
        var createResult = await controller.Create(new CreateAppointmentRequest(
            fixture.Client.Id,
            startTime,
            startTime.AddMinutes(30),
            null,
            regularService.Id,
            staffBusinessUser.Id));
        Assert.IsInstanceOfType(createResult, typeof(CreatedAtActionResult));
        var created = (AppointmentDto)((CreatedAtActionResult)createResult).Value!;

        var updateResult = await controller.Update(
            created.Id,
            new UpdateAppointmentRequest(
                default,
                default,
                AppointmentStatuses.Scheduled,
                null,
                imagingService.Id,
                null,
                PerformingTechnicianId: technician.Id));

        Assert.IsInstanceOfType(updateResult, typeof(OkObjectResult));
        var appointment = await context.Appointments.SingleAsync();
        var order = await context.ImagingOrders.SingleAsync();
        Assert.AreEqual(technicianBusinessUser.Id, appointment.StaffId);
        Assert.AreEqual(technician.Id, order.PerformingTechnicianId);
        Assert.AreEqual(imagingService.Id, order.ServiceId);
    }

    [TestMethod]
    public async Task SwitchingImagingAppointmentToNonImagingKeepsImagingAssignmentAndRequiresNormalStaff()
    {
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var technician = CreateUser(fixture.Tenant.Id, "Technician", true);
        var staff = CreateUser(fixture.Tenant.Id, "Staff", true);
        var technicianBusinessUser = CreateBusinessUser(technician);
        var staffBusinessUser = CreateBusinessUser(staff);
        var imagingService = CreateService(fixture.Tenant.Id, "Ultrasound", "US");
        var regularService = CreateService(fixture.Tenant.Id, "Consultation", null);
        context.Users.AddRange(technician, staff);
        context.BusinessUsers.AddRange(technicianBusinessUser, staffBusinessUser);
        context.Services.AddRange(imagingService, regularService);
        await context.SaveChangesAsync();

        var controller = CreateController(fixture, context);
        var startTime = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        var createResult = await controller.Create(new CreateAppointmentRequest(
            fixture.Client.Id,
            startTime,
            startTime.AddMinutes(30),
            null,
            imagingService.Id,
            null,
            PerformingTechnicianId: technician.Id));
        Assert.IsInstanceOfType(createResult, typeof(CreatedAtActionResult));
        var created = (AppointmentDto)((CreatedAtActionResult)createResult).Value!;
        var order = await context.ImagingOrders.SingleAsync();

        var missingStaffResult = await controller.Update(
            created.Id,
            new UpdateAppointmentRequest(
                default,
                default,
                AppointmentStatuses.Scheduled,
                null,
                regularService.Id,
                null));
        Assert.IsInstanceOfType(missingStaffResult, typeof(BadRequestObjectResult));

        var updateResult = await controller.Update(
            created.Id,
            new UpdateAppointmentRequest(
                default,
                default,
                AppointmentStatuses.Scheduled,
                null,
                regularService.Id,
                staffBusinessUser.Id,
                ClearPerformingTechnician: true));

        Assert.IsInstanceOfType(updateResult, typeof(OkObjectResult));
        var appointment = await context.Appointments.SingleAsync();
        Assert.AreEqual(staffBusinessUser.Id, appointment.StaffId);
        Assert.AreEqual(technician.Id, order.PerformingTechnicianId);
        Assert.AreEqual(imagingService.Id, order.ServiceId);
    }

    [TestMethod]
    public async Task LegacyImagingAppointmentDoesNotAssumeStaffIsTechnician()
    {
        await using var fixture = await QueueTestFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var legacyStaff = CreateUser(fixture.Tenant.Id, "Staff", true);
        var legacyStaffBusinessUser = CreateBusinessUser(legacyStaff);
        var service = CreateService(fixture.Tenant.Id, "X-Ray", "DX");
        var startTime = new DateTime(2026, 11, 18, 11, 0, 0, DateTimeKind.Utc);
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            ClientId = fixture.Client.Id,
            CreatedByUserId = fixture.User.Id,
            ServiceId = service.Id,
            StaffId = legacyStaffBusinessUser.Id,
            StartTime = startTime,
            EndTime = startTime.AddMinutes(30),
            AppointmentDate = startTime.Date,
            Status = AppointmentStatuses.Scheduled,
        };
        var order = new ImagingOrder
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            ClientId = fixture.Client.Id,
            AppointmentId = appointment.Id,
            ServiceId = service.Id,
            AccessionNumber = $"DX-{Guid.NewGuid():N}",
            Modality = "DX",
            Status = ImagingOrderStatuses.Scheduled,
            ScheduledStartTime = startTime,
        };
        context.Users.Add(legacyStaff);
        context.BusinessUsers.Add(legacyStaffBusinessUser);
        context.Services.Add(service);
        context.Appointments.Add(appointment);
        context.ImagingOrders.Add(order);
        await context.SaveChangesAsync();

        var updateResult = await CreateController(fixture, context).Update(
            appointment.Id,
            new UpdateAppointmentRequest(
                default,
                default,
                AppointmentStatuses.Scheduled,
                "Legacy note",
                service.Id,
                null));

        Assert.IsInstanceOfType(updateResult, typeof(BadRequestObjectResult));
        Assert.AreEqual(legacyStaffBusinessUser.Id, appointment.StaffId);
        Assert.IsNull(order.PerformingTechnicianId);
    }

    private static AppointmentsController CreateController(QueueTestFixture fixture, Clienta.Api.Data.AppDbContext context)
    {
        var controller = fixture.CreateController(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    private static User CreateUser(Guid tenantId, string role, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local",
        FullName = $"{role} Test User",
        PasswordHash = "hash",
        Role = role,
        IsActive = isActive,
    };

    private static BusinessUser CreateBusinessUser(User user) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = user.TenantId,
        UserId = user.Id,
    };

    private static Service CreateService(Guid tenantId, string name, string? modality) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        ImagingModality = modality,
        IsActive = true,
    };
}
