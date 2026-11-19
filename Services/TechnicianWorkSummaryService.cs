using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface ITechnicianWorkSummaryService
{
    Task<IReadOnlyList<TechnicianRateResponse>> GetRatesAsync(CancellationToken cancellationToken);
    Task<TechnicianRateResponse?> SetRateAsync(
        Guid technicianUserId,
        string modality,
        decimal amount,
        CancellationToken cancellationToken);
    Task<bool> DeactivateRateAsync(Guid rateId, CancellationToken cancellationToken);
    Task AddReceivedStudyWorkItemAsync(
        Guid imagingOrderId,
        Guid imagingStudyId,
        CancellationToken cancellationToken);
    Task<TechnicianWorkSummaryResponse?> GetSummaryAsync(
        Guid technicianUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}

public sealed class TechnicianWorkSummaryService : ITechnicianWorkSummaryService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;

    public TechnicianWorkSummaryService(AppDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<TechnicianRateResponse>> GetRatesAsync(CancellationToken cancellationToken)
    {
        var storedRates = await (
            from rate in _context.TechnicianRates.AsNoTracking()
            join user in _context.Users.AsNoTracking() on rate.TechnicianUserId equals user.Id
            where rate.TenantId == _tenant.TenantId && user.TenantId == _tenant.TenantId
            orderby user.FullName, rate.Modality, rate.IsActive descending
            select new { Rate = rate, UserName = user.FullName ?? user.Email })
            .ToListAsync(cancellationToken);

        return storedRates.Select(item => new TechnicianRateResponse(
            item.Rate.Id,
            item.Rate.TechnicianUserId,
            item.UserName,
            item.Rate.Modality,
            item.Rate.Amount,
            item.Rate.Currency,
            item.Rate.IsActive,
            StaffWorkSessionService.ToUtcOffset(item.Rate.CreatedAt),
            StaffWorkSessionService.ToUtcOffset(item.Rate.UpdatedAt)))
            .ToArray();
    }

    public async Task<TechnicianRateResponse?> SetRateAsync(
        Guid technicianUserId,
        string modality,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty || amount < 0 || !IsPayrollModality(modality))
            return null;

        var technician = await _context.Users.FirstOrDefaultAsync(user =>
            user.Id == technicianUserId &&
            user.TenantId == _tenant.TenantId &&
            user.IsActive &&
            user.Role == "Technician",
            cancellationToken);
        var tenant = await _context.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == _tenant.TenantId, cancellationToken);
        if (technician == null || tenant == null)
            return null;

        var normalizedModality = NormalizeRateModality(modality);
        var now = DateTime.UtcNow;
        var rate = await _context.TechnicianRates.FirstOrDefaultAsync(item =>
            item.TenantId == _tenant.TenantId &&
            item.TechnicianUserId == technicianUserId &&
            item.Modality == normalizedModality &&
            item.IsActive,
            cancellationToken);

        if (rate == null)
        {
            rate = new TechnicianRate
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                TechnicianUserId = technicianUserId,
                Modality = normalizedModality,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _context.TechnicianRates.Add(rate);
        }

        rate.Amount = amount;
        rate.Currency = tenant.Currency;
        rate.IsActive = true;
        rate.UpdatedAt = now;
        await _context.SaveChangesAsync(cancellationToken);

        return new TechnicianRateResponse(
            rate.Id,
            rate.TechnicianUserId,
            technician.FullName ?? technician.Email,
            rate.Modality,
            rate.Amount,
            rate.Currency,
            rate.IsActive,
            StaffWorkSessionService.ToUtcOffset(rate.CreatedAt),
            StaffWorkSessionService.ToUtcOffset(rate.UpdatedAt));
    }

    public async Task<bool> DeactivateRateAsync(Guid rateId, CancellationToken cancellationToken)
    {
        var rate = await _context.TechnicianRates.FirstOrDefaultAsync(item =>
            item.Id == rateId && item.TenantId == _tenant.TenantId && item.IsActive,
            cancellationToken);
        if (rate == null)
            return false;

        rate.IsActive = false;
        rate.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task AddReceivedStudyWorkItemAsync(
        Guid imagingOrderId,
        Guid imagingStudyId,
        CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty ||
            _context.TechnicianWorkItems.Local.Any(item =>
                item.TenantId == _tenant.TenantId && item.ImagingStudyId == imagingStudyId) ||
            await _context.TechnicianWorkItems.AnyAsync(item =>
                item.TenantId == _tenant.TenantId && item.ImagingStudyId == imagingStudyId,
                cancellationToken))
        {
            return;
        }

        var study = await _context.ImagingStudies.FirstOrDefaultAsync(item =>
            item.Id == imagingStudyId &&
            item.TenantId == _tenant.TenantId &&
            item.ImagingOrderId == imagingOrderId &&
            item.Status == ImagingStudyStatuses.Received,
            cancellationToken);
        if (study == null ||
            !await _context.ImagingInstances.AnyAsync(instance =>
                instance.TenantId == _tenant.TenantId && instance.ImagingStudyId == study.Id,
                cancellationToken))
        {
            return;
        }

        var order = await _context.ImagingOrders.FirstOrDefaultAsync(item =>
            item.Id == imagingOrderId &&
            item.TenantId == _tenant.TenantId &&
            item.ClientId == study.ClientId,
            cancellationToken);
        if (order?.PerformingTechnicianId is not Guid technicianUserId)
            return;

        var technicianIsEligible = await _context.Users.AnyAsync(user =>
            user.Id == technicianUserId &&
            user.TenantId == _tenant.TenantId &&
            user.IsActive &&
            user.Role == "Technician",
            cancellationToken);
        if (!technicianIsEligible)
            return;

        var originalModality = study.Modality;
        var payrollModality = NormalizePayrollModality(originalModality);
        var rate = payrollModality == null
            ? null
            : await _context.TechnicianRates.FirstOrDefaultAsync(item =>
                item.TenantId == _tenant.TenantId &&
                item.TechnicianUserId == technicianUserId &&
                item.Modality == payrollModality &&
                item.IsActive,
                cancellationToken);

        _context.TechnicianWorkItems.Add(new TechnicianWorkItem
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ImagingOrderId = order.Id,
            ImagingStudyId = study.Id,
            TechnicianUserId = technicianUserId,
            PerformedAt = study.ReceivedAt,
            OriginalModality = originalModality,
            PayrollModality = payrollModality,
            AccessionNumber = order.AccessionNumber,
            RateAmount = rate?.Amount,
            Currency = rate?.Currency,
            CreatedAt = DateTime.UtcNow,
        });
    }

    public async Task<TechnicianWorkSummaryResponse?> GetSummaryAsync(
        Guid technicianUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty ||
            fromUtc >= toUtc ||
            !await _context.Users.AsNoTracking().AnyAsync(user =>
                user.Id == technicianUserId &&
                user.TenantId == _tenant.TenantId &&
                user.Role == "Technician",
                cancellationToken))
        {
            return null;
        }

        var storedItems = await _context.TechnicianWorkItems
            .AsNoTracking()
            .Where(item =>
                item.TenantId == _tenant.TenantId &&
                item.TechnicianUserId == technicianUserId &&
                item.PerformedAt >= fromUtc.UtcDateTime &&
                item.PerformedAt < toUtc.UtcDateTime)
            .OrderBy(item => item.PerformedAt)
            .ToListAsync(cancellationToken);

        var items = storedItems.Select(item => new TechnicianWorkItemResponse(
                item.Id,
                item.ImagingOrderId,
                item.ImagingStudyId,
                StaffWorkSessionService.ToUtcOffset(item.PerformedAt),
                item.AccessionNumber,
                item.OriginalModality,
                item.PayrollModality,
                item.RateAmount,
                item.Currency))
            .ToArray();

        var counts = items
            .GroupBy(item => item.PayrollModality)
            .OrderBy(group => group.Key)
            .Select(group => new InterpreterModalityCountResponse(group.Key, group.Count()))
            .ToArray();
        var payable = items
            .Where(item => item.RateAmount.HasValue && !string.IsNullOrWhiteSpace(item.Currency))
            .GroupBy(item => item.Currency!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .Select(group => new InterpreterPayableTotalResponse(
                group.Key,
                group.Sum(item => item.RateAmount!.Value)))
            .ToArray();
        var withRate = items.Count(item => item.RateAmount.HasValue);

        return new TechnicianWorkSummaryResponse(
            technicianUserId,
            fromUtc.ToUniversalTime(),
            toUtc.ToUniversalTime(),
            items,
            items.Length,
            withRate,
            items.Length - withRate,
            counts,
            payable);
    }

    public static string? NormalizePayrollModality(string? originalModality) =>
        originalModality?.Trim().ToUpperInvariant() switch
        {
            "DX" or "DR" or "CR" => "XRay",
            "US" => "US",
            _ => null,
        };

    private static string NormalizeRateModality(string modality) =>
        modality.Trim().ToUpperInvariant() switch
        {
            "XRAY" => "XRay",
            "US" => "US",
            _ => string.Empty,
        };

    private static bool IsPayrollModality(string modality) =>
        modality.Trim().Equals("XRay", StringComparison.OrdinalIgnoreCase) ||
        modality.Trim().Equals("US", StringComparison.OrdinalIgnoreCase);
}
