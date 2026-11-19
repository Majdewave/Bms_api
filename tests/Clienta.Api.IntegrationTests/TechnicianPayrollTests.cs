using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class TechnicianPayrollTests
{
    [TestMethod]
    public async Task ReceivedStudyCreatesOneRateSnapshotAcrossRetriesAndAdditionalInstances()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync("DX");
        await using var context = fixture.CreateContext();
        var technician = CreateTechnician(fixture);
        context.Users.Add(technician);
        var order = await context.ImagingOrders.SingleAsync();
        order.PerformingTechnicianId = technician.Id;
        var rate = new TechnicianRate
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            TechnicianUserId = technician.Id,
            Modality = "XRay",
            Amount = 20,
            Currency = "ILS",
        };
        context.TechnicianRates.Add(rate);
        await context.SaveChangesAsync();

        var service = new TechnicianWorkSummaryService(context, fixture.TenantContext);
        await service.AddReceivedStudyWorkItemAsync(order.Id, fixture.Study.Id, CancellationToken.None);
        Assert.AreEqual(0, await context.TechnicianWorkItems.CountAsync());

        var series = await AddReceivedInstanceAsync(context, fixture, 1);
        await service.AddReceivedStudyWorkItemAsync(order.Id, fixture.Study.Id, CancellationToken.None);
        await context.SaveChangesAsync();

        rate.Amount = 35;
        await context.SaveChangesAsync();
        await AddReceivedInstanceAsync(context, fixture, 2, series.Id);
        await service.AddReceivedStudyWorkItemAsync(order.Id, fixture.Study.Id, CancellationToken.None);
        await context.SaveChangesAsync();

        var item = await context.TechnicianWorkItems.SingleAsync();
        Assert.AreEqual(20m, item.RateAmount);
        Assert.AreEqual("ILS", item.Currency);
        Assert.AreEqual("DX", item.OriginalModality);
        Assert.AreEqual("XRay", item.PayrollModality);
        Assert.AreEqual(fixture.Study.ReceivedAt, item.PerformedAt);
        Assert.AreEqual(order.AccessionNumber, item.AccessionNumber);

        var summary = await service.GetSummaryAsync(
            technician.Id,
            new DateTimeOffset(fixture.Study.ReceivedAt.AddMinutes(-1)),
            new DateTimeOffset(fixture.Study.ReceivedAt.AddDays(1)),
            CancellationToken.None);
        Assert.IsNotNull(summary);
        Assert.AreEqual(1, summary.TotalExaminations);
        Assert.AreEqual(1, summary.ExaminationsWithRate);
        Assert.AreEqual(0, summary.ExaminationsWithoutRate);
        Assert.AreEqual(20m, summary.PayableByCurrency.Single().Amount);
    }

    [TestMethod]
    public async Task ReceivedStudyWithoutRateIsCountedAsUnratedAndNotAsZeroPay()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync("US");
        await using var context = fixture.CreateContext();
        var technician = CreateTechnician(fixture);
        context.Users.Add(technician);
        var order = await context.ImagingOrders.SingleAsync();
        order.PerformingTechnicianId = technician.Id;
        await context.SaveChangesAsync();
        await AddReceivedInstanceAsync(context, fixture, 1);

        var service = new TechnicianWorkSummaryService(context, fixture.TenantContext);
        await service.AddReceivedStudyWorkItemAsync(order.Id, fixture.Study.Id, CancellationToken.None);
        await context.SaveChangesAsync();

        var item = await context.TechnicianWorkItems.SingleAsync();
        Assert.IsNull(item.RateAmount);
        Assert.IsNull(item.Currency);
        Assert.AreEqual("US", item.PayrollModality);

        var summary = await service.GetSummaryAsync(
            technician.Id,
            new DateTimeOffset(fixture.Study.ReceivedAt.AddMinutes(-1)),
            new DateTimeOffset(fixture.Study.ReceivedAt.AddDays(1)),
            CancellationToken.None);
        Assert.IsNotNull(summary);
        Assert.AreEqual(1, summary.ExaminationsWithoutRate);
        Assert.AreEqual(0, summary.PayableByCurrency.Count);
    }

    [TestMethod]
    public async Task TechnicianRatesAreTenantScopedAndUseTenantCurrency()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var technician = CreateTechnician(fixture);
        context.Users.Add(technician);
        await context.SaveChangesAsync();
        var service = new TechnicianWorkSummaryService(context, fixture.TenantContext);

        var rate = await service.SetRateAsync(
            technician.Id,
            "XRay",
            20,
            CancellationToken.None);
        Assert.IsNotNull(rate);
        Assert.AreEqual("XRay", rate.Modality);
        Assert.AreEqual(20m, rate.Amount);
        Assert.AreEqual(fixture.Tenant.Currency, rate.Currency);

        var otherTenantTechnicianId = Guid.NewGuid();
        Assert.IsNull(await service.SetRateAsync(
            otherTenantTechnicianId,
            "US",
            35,
            CancellationToken.None));
        Assert.IsNull(await service.SetRateAsync(
            fixture.Interpreter.Id,
            "US",
            35,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task OverviewIncludesTechnicianExaminationAndPayableMetrics()
    {
        await using var fixture = await WorkSummaryFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var technician = CreateTechnician(fixture);
        context.Users.Add(technician);
        context.TechnicianWorkItems.Add(new TechnicianWorkItem
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            ImagingOrderId = fixture.Order.Id,
            ImagingStudyId = fixture.Study.Id,
            TechnicianUserId = technician.Id,
            PerformedAt = fixture.Study.ReceivedAt,
            OriginalModality = "CR",
            PayrollModality = "XRay",
            AccessionNumber = fixture.Order.AccessionNumber,
            RateAmount = 20,
            Currency = "ILS",
        });
        await context.SaveChangesAsync();

        var overview = await new WorkSummaryOverviewService(context, fixture.TenantContext)
            .GetOverviewAsync(
                new DateTimeOffset(fixture.Study.ReceivedAt.AddMinutes(-1)),
                new DateTimeOffset(fixture.Study.ReceivedAt.AddDays(1)),
                CancellationToken.None);

        var member = overview.Members.Single(item => item.UserId == technician.Id);
        Assert.AreEqual("Technician", member.Role);
        Assert.AreEqual(1, member.TotalExaminations);
        Assert.AreEqual(1, member.ExaminationsWithRate);
        Assert.AreEqual(0, member.ExaminationsWithoutRate);
        Assert.AreEqual(1, member.XRayCount);
        Assert.AreEqual(20m, member.PayableByCurrency.Single().Amount);
        Assert.IsNull(member.TotalWorkedMinutes);
    }

    [DataTestMethod]
    [DataRow("DX", "XRay")]
    [DataRow("DR", "XRay")]
    [DataRow("CR", "XRay")]
    [DataRow("US", "US")]
    [DataRow("MR", null)]
    public void PayrollModalityMappingDoesNotChangeOriginalModality(string modality, string? expected)
    {
        Assert.AreEqual(expected, TechnicianWorkSummaryService.NormalizePayrollModality(modality));
    }

    private static User CreateTechnician(WorkSummaryFixture fixture) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = fixture.Tenant.Id,
        Email = $"technician-{Guid.NewGuid():N}@test.local",
        FullName = "Test Technician",
        PasswordHash = "hash",
        Role = "Technician",
        IsActive = true,
    };

    private static async Task<ImagingSeries> AddReceivedInstanceAsync(
        Clienta.Api.Data.AppDbContext context,
        WorkSummaryFixture fixture,
        int instanceNumber,
        Guid? seriesId = null)
    {
        var series = seriesId.HasValue
            ? await context.ImagingSeries.SingleAsync(item => item.Id == seriesId.Value)
            : new ImagingSeries
            {
                Id = Guid.NewGuid(),
                TenantId = fixture.Tenant.Id,
                ImagingStudyId = fixture.Study.Id,
                SeriesInstanceUID = $"series-{Guid.NewGuid():N}",
                Modality = fixture.Study.Modality,
                SeriesNumber = 1,
            };
        if (!seriesId.HasValue)
        {
            context.ImagingSeries.Add(series);
            await context.SaveChangesAsync();
        }

        context.ImagingInstances.Add(new ImagingInstance
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.Tenant.Id,
            ImagingStudyId = fixture.Study.Id,
            ImagingSeriesId = series.Id,
            SOPInstanceUID = $"sop-{Guid.NewGuid():N}",
            SOPClassUID = "test-sop-class",
            InstanceNumber = instanceNumber,
            LocalFilePath = $"instance-{instanceNumber}.dcm",
            StorageStatus = ImagingStudyStorageStatuses.Local,
        });
        await context.SaveChangesAsync();
        return series;
    }
}
