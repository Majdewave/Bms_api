using Amazon.S3;
using Clienta.Api.Controllers;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestPDF.Infrastructure;
using System.Security.Claims;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class WorkSummaryServiceTests
{
    [TestMethod]
    public async Task ClockInIsRestrictedToActiveStaffAndCannotCreateTwoOpenSessions()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var service = new StaffWorkSessionService(context, fixture.TenantContext);

        fixture.TenantContext.SetUserId(fixture.Staff.Id);
        var first = await service.ClockInAsync(CancellationToken.None);
        var duplicate = await service.ClockInAsync(CancellationToken.None);

        Assert.IsTrue(first.IsEligible);
        Assert.IsNotNull(first.NewSession);
        Assert.IsNotNull(duplicate.ExistingSession);
        Assert.AreEqual(first.NewSession.Id, duplicate.ExistingSession.Id);
        Assert.AreEqual(1, await context.StaffWorkSessions.CountAsync());

        fixture.TenantContext.SetUserId(fixture.Staff.Id);
        Assert.IsInstanceOfType(
            await fixture.CreateWorkSummaryController(context, authorized: true, "Staff").ClockIn(CancellationToken.None),
            typeof(ConflictObjectResult));

        fixture.TenantContext.SetUserId(fixture.Interpreter.Id);
        var interpreterClockIn = await service.ClockInAsync(CancellationToken.None);
        Assert.IsFalse(interpreterClockIn.IsEligible);
        Assert.IsNull(interpreterClockIn.NewSession);
    }

    [TestMethod]
    public async Task ClockOutClosesOnlyTheAuthenticatedStaffUsersOpenSession()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        context.StaffWorkSessions.Add(new StaffWorkSession
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            UserId = fixture.Admin.Id,
            CreatedByUserId = fixture.Admin.Id,
            ClockInAt = DateTime.UtcNow.AddHours(-1),
        });
        await context.SaveChangesAsync();
        var service = new StaffWorkSessionService(context, fixture.TenantContext);

        fixture.TenantContext.SetUserId(fixture.Staff.Id);
        var started = await service.ClockInAsync(CancellationToken.None);
        var finished = await service.ClockOutAsync(CancellationToken.None);

        Assert.IsNotNull(started.NewSession);
        Assert.IsNotNull(finished);
        Assert.IsTrue(finished.ClockOutAt.HasValue);
        Assert.AreEqual(fixture.Staff.Id, finished.ClockOutByUserId);
        Assert.IsNull(await service.GetOpenSessionAsync(CancellationToken.None));
        Assert.IsTrue(await context.StaffWorkSessions.AnyAsync(
            item => item.UserId == fixture.Admin.Id && item.ClockOutAt == null));
    }

    [TestMethod]
    public async Task ClockOutWithoutAnOpenSessionIsRejected()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var service = new StaffWorkSessionService(context, fixture.TenantContext);

        fixture.TenantContext.SetUserId(fixture.Staff.Id);

        Assert.IsNull(await service.ClockOutAsync(CancellationToken.None));
        Assert.IsInstanceOfType(
            await fixture.CreateWorkSummaryController(context, authorized: true, "Staff").ClockOut(CancellationToken.None),
            typeof(ConflictObjectResult));
    }

    [TestMethod]
    public async Task HoursSummaryClipsSessionToUtcRange()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        context.StaffWorkSessions.Add(new StaffWorkSession
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            UserId = fixture.Staff.Id,
            CreatedByUserId = fixture.Staff.Id,
            ClockOutByUserId = fixture.Staff.Id,
            ClockInAt = new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc),
            ClockOutAt = new DateTime(2026, 1, 2, 2, 0, 0, DateTimeKind.Utc),
        });
        context.StaffWorkSessions.Add(new StaffWorkSession
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            UserId = fixture.Staff.Id,
            CreatedByUserId = fixture.Staff.Id,
            ClockInAt = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc),
            ClockOutAt = null,
        });
        await context.SaveChangesAsync();

        var service = new StaffWorkSessionService(context, fixture.TenantContext);
        var summary = await service.GetHoursSummaryAsync(
            fixture.Staff.Id,
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsNotNull(summary);
        Assert.AreEqual(1, summary.CompletedSessions);
        Assert.AreEqual(1, summary.WorkingDays);
        Assert.AreEqual(120L, summary.TotalWorkedMinutes);
        Assert.AreEqual(new DateOnly(2026, 1, 1), summary.Sessions.Single().SessionDate);
    }

    [TestMethod]
    public async Task DatabaseUniqueIndexRejectsTwoOpenSessionsForSameTenantUser()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var now = DateTime.UtcNow;
        context.StaffWorkSessions.AddRange(
            new StaffWorkSession
            {
                Id = Guid.NewGuid(),
                TenantId = fixture.Tenant.Id,
                UserId = fixture.Staff.Id,
                CreatedByUserId = fixture.Staff.Id,
                ClockInAt = now.AddMinutes(-2),
            },
            new StaffWorkSession
            {
                Id = Guid.NewGuid(),
                TenantId = fixture.Tenant.Id,
                UserId = fixture.Staff.Id,
                CreatedByUserId = fixture.Staff.Id,
                ClockInAt = now.AddMinutes(-1),
            });

        await Assert.ThrowsExceptionAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [TestMethod]
    public async Task AttendanceQueriesAreTenantScoped()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Other Tenant",
            Subdomain = $"other-{Guid.NewGuid():N}",
        };
        var otherStaff = new User
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenant.Id,
            Email = "other-staff@test.local",
            PasswordHash = "hash",
            Role = "Staff",
            IsActive = true,
        };
        context.Tenants.Add(otherTenant);
        context.Users.Add(otherStaff);
        context.StaffWorkSessions.Add(new StaffWorkSession
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenant.Id,
            UserId = otherStaff.Id,
            CreatedByUserId = otherStaff.Id,
            ClockInAt = DateTime.UtcNow.AddHours(-1),
        });
        await context.SaveChangesAsync();

        var service = new StaffWorkSessionService(context, fixture.TenantContext);
        fixture.TenantContext.SetUserId(fixture.Staff.Id);
        Assert.IsNull(await service.GetOpenSessionAsync(CancellationToken.None));
        Assert.IsNull(await service.GetHoursSummaryAsync(
            otherStaff.Id,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            CancellationToken.None));
        Assert.AreEqual(0, await context.StaffWorkSessions.CountAsync());
    }

    [TestMethod]
    public async Task ManagerCorrectionRequiresAuthorizationAndValidReasonAndRange()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var session = new StaffWorkSession
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            UserId = fixture.Staff.Id,
            CreatedByUserId = fixture.Staff.Id,
            ClockInAt = DateTime.UtcNow.AddHours(-3),
        };
        context.StaffWorkSessions.Add(session);
        await context.SaveChangesAsync();

        fixture.TenantContext.SetUserId(fixture.Admin.Id);
        var unauthorized = fixture.CreateWorkSummaryController(context, authorized: false, "Staff");
        var validRequest = new CorrectStaffWorkSessionRequest
        {
            ClockInAt = DateTimeOffset.UtcNow.AddHours(-2),
            ClockOutAt = DateTimeOffset.UtcNow.AddHours(-1),
            Reason = "Approved correction",
        };
        Assert.IsInstanceOfType(
            await unauthorized.CorrectSession(fixture.Staff.Id, session.Id, validRequest, CancellationToken.None),
            typeof(ForbidResult));

        var authorized = fixture.CreateWorkSummaryController(context, authorized: true, "Admin");
        Assert.IsInstanceOfType(
            await authorized.CorrectSession(
                fixture.Staff.Id,
                session.Id,
                validRequest with { Reason = "   " },
                CancellationToken.None),
            typeof(BadRequestObjectResult));
        Assert.IsInstanceOfType(
            await authorized.CorrectSession(
                fixture.Staff.Id,
                session.Id,
                validRequest with
                {
                    ClockInAt = DateTimeOffset.UtcNow.AddHours(-1),
                    ClockOutAt = DateTimeOffset.UtcNow.AddHours(-2),
                },
                CancellationToken.None),
            typeof(BadRequestObjectResult));
        Assert.IsInstanceOfType(
            await authorized.CorrectSession(
                fixture.Staff.Id,
                session.Id,
                validRequest with { ClockInAt = default },
                CancellationToken.None),
            typeof(BadRequestObjectResult));

        var corrected = await authorized.CorrectSession(
            fixture.Staff.Id,
            session.Id,
            validRequest,
            CancellationToken.None);
        Assert.IsInstanceOfType<OkObjectResult>(corrected);
        var updated = await context.StaffWorkSessions.SingleAsync(item => item.Id == session.Id);
        Assert.AreEqual("Approved correction", updated.CorrectionReason);
        Assert.AreEqual(fixture.Admin.Id, updated.LastCorrectedByUserId);
    }

    [TestMethod]
    public void PayrollModalityNormalizationIsLimitedToPayrollMapping()
    {
        Assert.AreEqual("XRay", InterpreterWorkSummaryService.NormalizePayrollModality("DX"));
        Assert.AreEqual("XRay", InterpreterWorkSummaryService.NormalizePayrollModality("DR"));
        Assert.AreEqual("XRay", InterpreterWorkSummaryService.NormalizePayrollModality("CR"));
        Assert.AreEqual("US", InterpreterWorkSummaryService.NormalizePayrollModality("US"));
        Assert.IsNull(InterpreterWorkSummaryService.NormalizePayrollModality("MR"));
    }

    [TestMethod]
    public async Task RatesAreTenantScopedInterpreterOnlyAndSeparateByModality()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var currentTenant = await context.Tenants.SingleAsync();
        currentTenant.Currency = "USD";
        await context.SaveChangesAsync();
        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Other Tenant",
            Subdomain = $"other-{Guid.NewGuid():N}",
        };
        var otherInterpreter = new User
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenant.Id,
            Email = "other-interpreter@test.local",
            PasswordHash = "hash",
            Role = "Interpreter",
        };
        context.Tenants.Add(otherTenant);
        context.Users.Add(otherInterpreter);
        await context.SaveChangesAsync();

        var service = new InterpreterWorkSummaryService(context, fixture.TenantContext);
        Assert.IsNull(await service.SetRateAsync(otherInterpreter.Id, "XRay", 10m, CancellationToken.None));
        Assert.IsNull(await service.SetRateAsync(fixture.Staff.Id, "US", 10m, CancellationToken.None));

        var xray = await service.SetRateAsync(fixture.Interpreter.Id, "XRay", 20m, CancellationToken.None);
        var ultrasound = await service.SetRateAsync(fixture.Interpreter.Id, "US", 30m, CancellationToken.None);
        Assert.IsNotNull(xray);
        Assert.IsNotNull(ultrasound);
        Assert.AreNotEqual(xray.Id, ultrasound.Id);
        Assert.AreEqual("USD", xray.Currency);
        Assert.AreEqual(2, (await service.GetRatesAsync(CancellationToken.None)).Count);

        fixture.TenantContext.SetTenant(otherTenant.Id);
        fixture.TenantContext.SetUserId(otherInterpreter.Id);
        Assert.AreEqual(0, (await service.GetRatesAsync(CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task CompletionSnapshotsMappedRateAndSurvivesRequestDeletion()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync("DX");
        await using var context = fixture.CreateContext();
        context.InterpreterRates.Add(new InterpreterRate
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            InterpreterUserId = fixture.Interpreter.Id,
            Modality = "XRay",
            Amount = 25m,
            Currency = "ILS",
            IsActive = true,
        });
        await context.SaveChangesAsync();

        var request = await context.InterpretationRequests
            .Include(item => item.ImagingStudy)
            .Include(item => item.ImagingOrder)
            .SingleAsync(item => item.Id == fixture.Request.Id);
        request.Status = InterpretationRequestStatuses.Completed;
        request.CompletedAt = new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        var service = new InterpreterWorkSummaryService(context, fixture.TenantContext);
        var completedAt = request.CompletedAt.Value;
        await service.AddCompletedWorkItemAsync(request, completedAt, CancellationToken.None);
        await context.SaveChangesAsync();

        context.InterpretationRequests.Remove(request);
        await context.SaveChangesAsync();

        var summary = await service.GetSummaryAsync(
            fixture.Interpreter.Id,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsNotNull(summary);
        Assert.AreEqual(1, summary.TotalInterpretations);
        Assert.AreEqual(1, summary.InterpretationsWithRate);
        Assert.AreEqual("XRay", summary.Items.Single().PayrollModality);
        Assert.AreEqual(25m, summary.Items.Single().RateAmount);
        Assert.AreEqual("ILS", summary.PayableByCurrency.Single().Currency);
        Assert.AreEqual(25m, summary.PayableByCurrency.Single().Amount);
        Assert.AreEqual("DX", (await context.ImagingStudies.AsNoTracking().SingleAsync()).Modality);
    }

    [TestMethod]
    public async Task UnconfiguredRateCreatesNullPayableAndRateChangesDoNotRewriteSnapshot()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync("US");
        await using var context = fixture.CreateContext();
        var request = await context.InterpretationRequests
            .Include(item => item.ImagingStudy)
            .Include(item => item.ImagingOrder)
            .SingleAsync(item => item.Id == fixture.Request.Id);
        request.Status = InterpretationRequestStatuses.Completed;
        request.CompletedAt = DateTime.UtcNow;
        var service = new InterpreterWorkSummaryService(context, fixture.TenantContext);

        await service.AddCompletedWorkItemAsync(request, request.CompletedAt.Value, CancellationToken.None);
        await service.AddCompletedWorkItemAsync(request, request.CompletedAt.Value, CancellationToken.None);
        await context.SaveChangesAsync();

        var snapshot = await context.InterpreterWorkItems.SingleAsync();
        Assert.AreEqual("US", snapshot.PayrollModality);
        Assert.IsNull(snapshot.RateAmount);
        Assert.AreEqual("ILS", snapshot.Currency);

        await service.SetRateAsync(fixture.Interpreter.Id, "US", 99m, CancellationToken.None);
        var unchanged = await context.InterpreterWorkItems.AsNoTracking().SingleAsync();
        Assert.IsNull(unchanged.RateAmount);
        Assert.AreEqual(1, await context.InterpreterWorkItems.CountAsync());
    }

    [TestMethod]
    public async Task InProgressRequestCannotBeRecordedAsCompletedWork()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var request = await context.InterpretationRequests
            .Include(item => item.ImagingStudy)
            .Include(item => item.ImagingOrder)
            .SingleAsync(item => item.Id == fixture.Request.Id);
        var service = new InterpreterWorkSummaryService(context, fixture.TenantContext);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.AddCompletedWorkItemAsync(request, DateTime.UtcNow, CancellationToken.None));
        Assert.AreEqual(0, await context.InterpreterWorkItems.CountAsync());
    }

    [TestMethod]
    public async Task SuccessfulInterpretationCompletionSnapshotsOnceAndRetryDoesNotDuplicate()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync("DX");
        await using var context = fixture.CreateContext();
        var report = new InterpretationReport
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            InterpretationRequestId = fixture.Request.Id,
            Content = "Imaging findings",
        };
        context.InterpretationReports.Add(report);
        context.InterpreterRates.Add(new InterpreterRate
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            InterpreterUserId = fixture.Interpreter.Id,
            Modality = "XRay",
            Amount = 42m,
            Currency = "ILS",
            IsActive = true,
        });
        await context.SaveChangesAsync();

        QuestPDF.Settings.License = LicenseType.Community;
        fixture.TenantContext.SetUserId(fixture.Interpreter.Id);
        var controller = fixture.CreateInterpreterController(context);
        var completed = await controller.CompleteInterpretation(fixture.Request.Id, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(completed);
        var snapshot = await context.InterpreterWorkItems.SingleAsync();
        var request = await context.InterpretationRequests.SingleAsync();
        Assert.AreEqual(request.Id, snapshot.InterpretationRequestId);
        Assert.AreEqual(fixture.Interpreter.Id, snapshot.InterpreterUserId);
        Assert.AreEqual(request.CompletedAt, snapshot.CompletedAt);
        Assert.AreEqual("DX", snapshot.OriginalModality);
        Assert.AreEqual("XRay", snapshot.PayrollModality);
        Assert.AreEqual("WS-001", snapshot.AccessionNumber);
        Assert.AreEqual(42m, snapshot.RateAmount);
        Assert.AreEqual("ILS", snapshot.Currency);

        await new InterpreterWorkSummaryService(context, fixture.TenantContext)
            .SetRateAsync(fixture.Interpreter.Id, "XRay", 99m, CancellationToken.None);
        Assert.AreEqual(
            42m,
            (await context.InterpreterWorkItems.AsNoTracking().SingleAsync()).RateAmount);

        Assert.IsInstanceOfType(
            await controller.CompleteInterpretation(fixture.Request.Id, CancellationToken.None),
            typeof(OkObjectResult));
        Assert.AreEqual(1, await context.InterpreterWorkItems.CountAsync());
    }

    [TestMethod]
    public async Task CompletionWithoutReportDoesNotCreateWorkItem()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        fixture.TenantContext.SetUserId(fixture.Interpreter.Id);
        var result = await fixture.CreateInterpreterController(context)
            .CompleteInterpretation(fixture.Request.Id, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        Assert.AreEqual(0, await context.InterpreterWorkItems.CountAsync());
        Assert.AreEqual(0, await context.InterpretationReportDocuments.CountAsync());
        Assert.AreEqual(
            InterpretationRequestStatuses.InProgress,
            (await context.InterpretationRequests.SingleAsync()).Status);
    }

    [TestMethod]
    public async Task SuccessfulCompletionWithoutConfiguredRateCreatesUnratedWorkItem()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync("US");
        await using var context = fixture.CreateContext();
        context.InterpretationReports.Add(new InterpretationReport
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            InterpretationRequestId = fixture.Request.Id,
            Content = "Ultrasound findings",
        });
        await context.SaveChangesAsync();

        QuestPDF.Settings.License = LicenseType.Community;
        fixture.TenantContext.SetUserId(fixture.Interpreter.Id);
        var completed = await fixture.CreateInterpreterController(context)
            .CompleteInterpretation(fixture.Request.Id, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(completed);
        var item = await context.InterpreterWorkItems.SingleAsync();
        Assert.AreEqual("US", item.PayrollModality);
        Assert.IsNull(item.RateAmount);
        Assert.AreEqual("ILS", item.Currency);
    }

    [TestMethod]
    public async Task InterpreterSummaryUsesCompletedAtAndSnapshottedPayableAmounts()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        context.InterpreterWorkItems.AddRange(
            CreateWorkItem(fixture, "DX", "XRay", from.AddDays(1), 12m),
            CreateWorkItem(fixture, "CR", "XRay", from.AddDays(2), null),
            CreateWorkItem(fixture, "US", "US", from.AddDays(3), 18m),
            CreateWorkItem(fixture, "US", "US", to, 500m));
        await context.SaveChangesAsync();

        var summary = await new InterpreterWorkSummaryService(context, fixture.TenantContext)
            .GetSummaryAsync(
                fixture.Interpreter.Id,
                new DateTimeOffset(from),
                new DateTimeOffset(to),
                CancellationToken.None);

        Assert.IsNotNull(summary);
        Assert.AreEqual(3, summary.TotalInterpretations);
        Assert.AreEqual(2, summary.InterpretationsWithRate);
        Assert.AreEqual(1, summary.InterpretationsWithoutRate);
        Assert.AreEqual(2, summary.CountsByPayrollModality.Single(item => item.PayrollModality == "XRay").Count);
        Assert.AreEqual(1, summary.CountsByPayrollModality.Single(item => item.PayrollModality == "US").Count);
        Assert.AreEqual(30m, summary.PayableByCurrency.Single(item => item.Currency == "ILS").Amount);
    }

    [TestMethod]
    public async Task OverviewAggregatesSelectedTenantAndExcludesOpenSessionsFromHours()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);
        context.StaffWorkSessions.AddRange(
            new StaffWorkSession
            {
                Id = Guid.NewGuid(),
                TenantId = fixture.Tenant.Id,
                UserId = fixture.Staff.Id,
                CreatedByUserId = fixture.Staff.Id,
                ClockInAt = from.AddHours(9),
                ClockOutAt = from.AddHours(11),
            },
            new StaffWorkSession
            {
                Id = Guid.NewGuid(),
                TenantId = fixture.Tenant.Id,
                UserId = fixture.Staff.Id,
                CreatedByUserId = fixture.Staff.Id,
                ClockInAt = from.AddHours(12),
            });
        context.InterpreterWorkItems.AddRange(
            CreateWorkItem(fixture, "DX", "XRay", from.AddHours(13), 12m),
            CreateWorkItem(fixture, "US", "US", from.AddHours(14), null),
            CreateWorkItem(fixture, "US", "US", to, 500m));

        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Other Tenant",
            Subdomain = $"other-{Guid.NewGuid():N}",
        };
        var otherInterpreter = new User
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenant.Id,
            Email = "other-interpreter@test.local",
            PasswordHash = "hash",
            Role = "Interpreter",
        };
        context.Tenants.Add(otherTenant);
        context.Users.Add(otherInterpreter);
        context.InterpreterWorkItems.Add(new InterpreterWorkItem
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenant.Id,
            InterpretationRequestId = Guid.NewGuid(),
            ImagingOrderId = Guid.NewGuid(),
            ImagingStudyId = Guid.NewGuid(),
            InterpreterUserId = otherInterpreter.Id,
            CompletedAt = from.AddHours(15),
            OriginalModality = "DX",
            PayrollModality = "XRay",
            AccessionNumber = "OTHER-TENANT",
            RateAmount = 999m,
            Currency = "USD",
        });
        await context.SaveChangesAsync();

        var overview = await new WorkSummaryOverviewService(context, fixture.TenantContext)
            .GetOverviewAsync(
                new DateTimeOffset(from),
                new DateTimeOffset(to),
                CancellationToken.None);

        Assert.AreEqual(2, overview.MemberCount);
        Assert.AreEqual(1, overview.CompletedSessions);
        Assert.AreEqual(1, overview.WorkingDays);
        Assert.AreEqual(120L, overview.TotalWorkedMinutes);
        Assert.AreEqual(2, overview.TotalInterpretations);
        Assert.AreEqual(1, overview.InterpretationsWithRate);
        Assert.AreEqual(1, overview.InterpretationsWithoutRate);
        Assert.AreEqual(12m, overview.PayableByCurrency.Single().Amount);

        var staff = overview.Members.Single(member => member.Role == "Staff");
        Assert.IsTrue(staff.IsClockedIn);
        Assert.AreEqual(1, staff.CompletedSessions);
        Assert.AreEqual(120L, staff.TotalWorkedMinutes);

        var interpreter = overview.Members.Single(member => member.Role == "Interpreter");
        Assert.AreEqual(2, interpreter.TotalInterpretations);
        Assert.AreEqual(1, interpreter.XRayCount);
        Assert.AreEqual(1, interpreter.USCount);
        Assert.AreEqual(1, interpreter.InterpretationsWithoutRate);
    }

    [TestMethod]
    public async Task OverviewEndpointRequiresManagerAuthorizationAndValidRange()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;

        var unauthorized = fixture.CreateWorkSummaryController(context, authorized: false, "Admin");
        Assert.IsInstanceOfType(
            await unauthorized.GetOverview(from, to, CancellationToken.None),
            typeof(ForbidResult));

        var authorized = fixture.CreateWorkSummaryController(context, authorized: true, "Admin");
        Assert.IsInstanceOfType(
            await authorized.GetOverview(from, from, CancellationToken.None),
            typeof(BadRequestObjectResult));
        Assert.IsInstanceOfType(
            await authorized.GetOverview(from, to, CancellationToken.None),
            typeof(OkObjectResult));
    }

    [TestMethod]
    public async Task UnsupportedModalityStillCreatesUnratedWorkItem()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync("MR");
        await using var context = fixture.CreateContext();
        var request = await context.InterpretationRequests
            .Include(item => item.ImagingStudy)
            .Include(item => item.ImagingOrder)
            .SingleAsync(item => item.Id == fixture.Request.Id);
        request.Status = InterpretationRequestStatuses.Completed;
        request.CompletedAt = DateTime.UtcNow;
        var service = new InterpreterWorkSummaryService(context, fixture.TenantContext);

        await service.AddCompletedWorkItemAsync(request, request.CompletedAt.Value, CancellationToken.None);
        await context.SaveChangesAsync();

        var item = await context.InterpreterWorkItems.SingleAsync();
        Assert.AreEqual("MR", item.OriginalModality);
        Assert.IsNull(item.PayrollModality);
        Assert.IsNull(item.RateAmount);
    }

    private static InterpreterWorkItem CreateWorkItem(
        WorkSummaryFixture fixture,
        string originalModality,
        string? payrollModality,
        DateTime completedAt,
        decimal? rateAmount) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = fixture.Tenant.Id,
        InterpretationRequestId = Guid.NewGuid(),
        ImagingOrderId = Guid.NewGuid(),
        ImagingStudyId = Guid.NewGuid(),
        InterpreterUserId = fixture.Interpreter.Id,
        CompletedAt = completedAt,
        OriginalModality = originalModality,
        PayrollModality = payrollModality,
        AccessionNumber = $"WS-{Guid.NewGuid():N}",
        RateAmount = rateAmount,
        Currency = "ILS",
    };
}

internal sealed class WorkSummaryFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private WorkSummaryFixture(SqliteConnection connection) => _connection = connection;

    public required Tenant Tenant { get; init; }
    public required User Staff { get; init; }
    public required User Admin { get; init; }
    public required User Interpreter { get; init; }
    public required Client Client { get; init; }
    public required ImagingStudy Study { get; init; }
    public required ImagingOrder Order { get; init; }
    public required InterpretationRequest Request { get; init; }
    public required TestWorkTenantContext TenantContext { get; init; }

    public static async Task<WorkSummaryFixture> CreateAsync(string modality = "US")
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var fixture = new WorkSummaryFixture(connection)
        {
            TenantContext = new TestWorkTenantContext(),
            Tenant = new Tenant { Id = Guid.NewGuid(), Name = "Work Summary Tenant", Subdomain = $"work-{Guid.NewGuid():N}" },
            Staff = new User
            {
                Id = Guid.NewGuid(),
                Email = "staff@test.local",
                PasswordHash = "hash",
                Role = "Staff",
                IsActive = true,
            },
            Admin = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@test.local",
                PasswordHash = "hash",
                Role = "Admin",
                IsActive = true,
            },
            Interpreter = new User
            {
                Id = Guid.NewGuid(),
                Email = "interpreter@test.local",
                PasswordHash = "hash",
                Role = "Interpreter",
                IsActive = true,
            },
            Client = new Client { Id = Guid.NewGuid(), FullName = "Work Summary Client" },
            Study = new ImagingStudy
            {
                Id = Guid.NewGuid(),
                AccessionNumber = "WS-001",
                StudyInstanceUID = $"study-{Guid.NewGuid():N}",
                Modality = modality,
                LocalStoragePath = string.Empty,
                StorageStatus = ImagingStudyStorageStatuses.Local,
            },
            Order = new ImagingOrder
            {
                Id = Guid.NewGuid(),
                AccessionNumber = "WS-001",
                Modality = modality,
                ScheduledStartTime = DateTime.UtcNow,
            },
            Request = new InterpretationRequest
            {
                Id = Guid.NewGuid(),
                Status = InterpretationRequestStatuses.InProgress,
            },
        };

        fixture.Staff.TenantId = fixture.Tenant.Id;
        fixture.Admin.TenantId = fixture.Tenant.Id;
        fixture.Interpreter.TenantId = fixture.Tenant.Id;
        fixture.Client.TenantId = fixture.Tenant.Id;
        fixture.Study.TenantId = fixture.Tenant.Id;
        fixture.Study.ClientId = fixture.Client.Id;
        fixture.Study.ImagingOrderId = fixture.Order.Id;
        fixture.Order.TenantId = fixture.Tenant.Id;
        fixture.Order.ClientId = fixture.Client.Id;
        fixture.Request.TenantId = fixture.Tenant.Id;
        fixture.Request.ImagingOrderId = fixture.Order.Id;
        fixture.Request.ImagingStudyId = fixture.Study.Id;
        fixture.Request.AssignedInterpreterId = fixture.Interpreter.Id;
        fixture.Request.RequestedByUserId = fixture.Staff.Id;
        fixture.TenantContext.SetTenant(fixture.Tenant.Id);
        fixture.TenantContext.SetUserId(fixture.Staff.Id);

        await using var context = fixture.CreateContext();
        await context.Database.EnsureCreatedAsync();
        context.Tenants.Add(fixture.Tenant);
        context.Users.AddRange(fixture.Staff, fixture.Admin, fixture.Interpreter);
        context.Clients.Add(fixture.Client);
        await context.SaveChangesAsync();
        context.ImagingStudies.Add(fixture.Study);
        context.ImagingOrders.Add(fixture.Order);
        await context.SaveChangesAsync();
        context.InterpretationRequests.Add(fixture.Request);
        await context.SaveChangesAsync();
        return fixture;
    }

    public AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options,
        TenantContext);

    public InterpreterPortalController CreateInterpreterController(AppDbContext context)
    {
        var configuration = new ConfigurationBuilder().Build();
        var controller = new InterpreterPortalController(
            context,
            TenantContext,
            (IAmazonS3)null!,
            NullLogger<InterpreterPortalController>.Instance,
            configuration,
            new InterpretationPdfService((IAmazonS3)null!, configuration, NullLogger<InterpretationPdfService>.Instance),
            new InterpreterWorkSummaryService(context, TenantContext));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, "Interpreter"),
                }, "test")),
            },
        };
        return controller;
    }

    public WorkSummaryController CreateWorkSummaryController(
        AppDbContext context,
        bool authorized,
        string role)
    {
        var controller = new WorkSummaryController(
            context,
            TenantContext,
            new TestAuthorizationService(authorized),
            new StaffWorkSessionService(context, TenantContext),
            new InterpreterWorkSummaryService(context, TenantContext),
            new TechnicianWorkSummaryService(context, TenantContext),
            new WorkSummaryOverviewService(context, TenantContext));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, role),
                }, "test")),
            },
        };
        return controller;
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}

internal sealed class TestWorkTenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public void SetTenant(Guid tenantId) => TenantId = tenantId;
    public void SetUserId(Guid userId) => UserId = userId;
}

internal sealed class TestAuthorizationService(bool authorized) : IAuthorizationService
{
    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        object? resource,
        IEnumerable<IAuthorizationRequirement> requirements) =>
        Task.FromResult(authorized ? AuthorizationResult.Success() : AuthorizationResult.Failed());

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        object? resource,
        string policyName) =>
        Task.FromResult(authorized ? AuthorizationResult.Success() : AuthorizationResult.Failed());
}
