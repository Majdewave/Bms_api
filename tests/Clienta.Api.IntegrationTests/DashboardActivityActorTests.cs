using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class DashboardActivityActorTests
{
    [TestMethod]
    public async Task RecentActivity_UsesRecordedCreatorAndDoesNotGuessMissingActors()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim(), isDeterministic: true);

        var tenantId = Guid.NewGuid();
        var creator = new User { Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Appointment creator", Email = "creator@example.test" };
        var completionActor = new User { Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Appointment completer", Email = "completer@example.test" };
        var assignedStaffUser = new User { Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Assigned staff", Email = "staff@example.test" };
        var tenantContext = new TestTenantContext(tenantId, creator.Id);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        var tenant = new Tenant { Id = tenantId, Name = "Dashboard activity test", Subdomain = $"activity-{Guid.NewGuid():N}" };
        var client = new Client { Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Test client", CreatedAt = DateTime.UtcNow.AddHours(-3) };
        var assignedStaff = new BusinessUser { Id = Guid.NewGuid(), TenantId = tenantId, UserId = assignedStaffUser.Id };
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = client.Id,
            CreatedByUserId = creator.Id,
            StaffId = assignedStaff.Id,
            StartTime = DateTime.UtcNow.AddHours(-4),
            EndTime = DateTime.UtcNow.AddHours(-2),
            AppointmentDate = DateTime.UtcNow.Date,
            Status = "Completed",
            CreatedAt = DateTime.UtcNow.AddHours(-4)
        };
        var legacyCompletedAppointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = client.Id,
            CreatedByUserId = creator.Id,
            StaffId = assignedStaff.Id,
            StartTime = DateTime.UtcNow.AddHours(-5),
            EndTime = DateTime.UtcNow.AddHours(-3),
            AppointmentDate = DateTime.UtcNow.Date,
            Status = "Completed",
            CreatedAt = DateTime.UtcNow.AddHours(-5)
        };
        var visitSummary = new VisitSummary
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = client.Id,
            StaffId = assignedStaffUser.Id,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        db.Users.AddRange(creator, completionActor, assignedStaffUser);
        await db.SaveChangesAsync();
        tenant.OwnerUserId = creator.Id;
        await db.SaveChangesAsync();
        db.Clients.Add(client);
        db.BusinessUsers.Add(assignedStaff);
        db.Appointments.Add(appointment);
        db.Appointments.Add(legacyCompletedAppointment);
        db.VisitSummaries.Add(visitSummary);
        await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = completionActor.Id,
            EntityName = nameof(Appointment),
            ActionType = "appointment_completed",
            EntityId = appointment.Id.ToString(),
            PerformedBy = "Staff - Appointment completer",
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        await db.SaveChangesAsync();

        var service = new DashboardService(db, tenantContext, new OwnerDepartmentAccessService(tenantId, creator.Id));
        var events = (await service.GetRecentActivityAsync()).ToList();

        Assert.AreEqual("Appointment creator", events.Single(activity => activity.type == "appointment_created" && activity.id == appointment.Id).performedBy);
        Assert.AreEqual("Staff - Appointment completer", events.Single(activity => activity.type == "appointment_completed" && activity.id == appointment.Id).performedBy,
            "Completion activity must use the recorded actor, not infer the assigned staff member.");
        Assert.IsNull(events.Single(activity => activity.type == "appointment_completed" && activity.id == legacyCompletedAppointment.Id).performedBy,
            "Historical completion without a recorded actor must remain unavailable.");
        Assert.AreEqual("Assigned staff", events.Single(activity => activity.type == "visit_summary_created").performedBy);
        Assert.IsNull(events.Single(activity => activity.type == "client_created").performedBy,
            "Client does not persist the user who created it.");
    }

    private sealed class TestTenantContext(Guid tenantId, Guid userId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
        public void SetTenant(Guid value) { }
        public void SetUserId(Guid value) { }
    }

    private sealed class OwnerDepartmentAccessService(Guid tenantId, Guid userId) : IDepartmentAccessService
    {
        public Task<DepartmentAccessContext> GetCurrentUserAccessContextAsync() =>
            Task.FromResult(new DepartmentAccessContext { TenantId = tenantId, UserId = userId, IsOwner = true });

        public IQueryable<Appointment> ApplyAppointmentVisibility(IQueryable<Appointment> query, DepartmentAccessContext accessContext) => query;

        public IQueryable<VisitSummary> ApplyVisitSummaryVisibility(IQueryable<VisitSummary> query, DepartmentAccessContext accessContext) => query;
    }
}
