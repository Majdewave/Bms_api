using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IInterpreterWorkSummaryService
{
    Task<IReadOnlyList<InterpreterRateResponse>> GetRatesAsync(CancellationToken cancellationToken);
    Task<InterpreterRateResponse?> SetRateAsync(
        Guid interpreterUserId,
        string modality,
        decimal amount,
        CancellationToken cancellationToken);
    Task<bool> DeactivateRateAsync(Guid rateId, CancellationToken cancellationToken);
    Task AddCompletedWorkItemAsync(
        InterpretationRequest request,
        DateTime completedAt,
        CancellationToken cancellationToken);
    Task<InterpreterWorkSummaryResponse?> GetSummaryAsync(
        Guid interpreterUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}

public sealed class InterpreterWorkSummaryService : IInterpreterWorkSummaryService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;

    public InterpreterWorkSummaryService(AppDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<InterpreterRateResponse>> GetRatesAsync(CancellationToken cancellationToken)
    {
        var storedRates = await (
            from rate in _context.InterpreterRates.AsNoTracking()
            join user in _context.Users.AsNoTracking() on rate.InterpreterUserId equals user.Id
            where rate.TenantId == _tenant.TenantId && user.TenantId == _tenant.TenantId
            orderby user.FullName, rate.Modality, rate.IsActive descending
            select new { Rate = rate, UserName = user.FullName ?? user.Email })
            .ToListAsync(cancellationToken);

        return storedRates.Select(item => new InterpreterRateResponse(
            item.Rate.Id,
            item.Rate.InterpreterUserId,
            item.UserName,
            item.Rate.Modality,
            item.Rate.Amount,
            item.Rate.Currency,
            item.Rate.IsActive,
            StaffWorkSessionService.ToUtcOffset(item.Rate.CreatedAt),
            StaffWorkSessionService.ToUtcOffset(item.Rate.UpdatedAt)))
            .ToArray();
    }

    public async Task<InterpreterRateResponse?> SetRateAsync(
        Guid interpreterUserId,
        string modality,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty || amount < 0 ||
            !IsPayrollModality(modality))
        {
            return null;
        }

        var interpreter = await _context.Users.FirstOrDefaultAsync(user =>
            user.Id == interpreterUserId &&
            user.TenantId == _tenant.TenantId &&
            user.IsActive &&
            user.Role == "Interpreter",
            cancellationToken);
        var tenant = await _context.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == _tenant.TenantId, cancellationToken);
        if (interpreter == null || tenant == null)
            return null;

        var normalizedModality = NormalizeRateModality(modality);
        var now = DateTime.UtcNow;
        var rate = await _context.InterpreterRates.FirstOrDefaultAsync(item =>
            item.TenantId == _tenant.TenantId &&
            item.InterpreterUserId == interpreterUserId &&
            item.Modality == normalizedModality &&
            item.IsActive,
            cancellationToken);

        if (rate == null)
        {
            rate = new InterpreterRate
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                InterpreterUserId = interpreterUserId,
                Modality = normalizedModality,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _context.InterpreterRates.Add(rate);
        }

        rate.Amount = amount;
        rate.Currency = tenant.Currency;
        rate.IsActive = true;
        rate.UpdatedAt = now;
        await _context.SaveChangesAsync(cancellationToken);

        return new InterpreterRateResponse(
            rate.Id,
            rate.InterpreterUserId,
            interpreter.FullName ?? interpreter.Email,
            rate.Modality,
            rate.Amount,
            rate.Currency,
            rate.IsActive,
            StaffWorkSessionService.ToUtcOffset(rate.CreatedAt),
            StaffWorkSessionService.ToUtcOffset(rate.UpdatedAt));
    }

    public async Task<bool> DeactivateRateAsync(Guid rateId, CancellationToken cancellationToken)
    {
        var rate = await _context.InterpreterRates.FirstOrDefaultAsync(item =>
            item.Id == rateId && item.TenantId == _tenant.TenantId && item.IsActive,
            cancellationToken);
        if (rate == null)
            return false;

        rate.IsActive = false;
        rate.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task AddCompletedWorkItemAsync(
        InterpretationRequest request,
        DateTime completedAt,
        CancellationToken cancellationToken)
    {
        if (request.TenantId != _tenant.TenantId ||
            !string.Equals(
                request.Status,
                InterpretationRequestStatuses.Completed,
                StringComparison.OrdinalIgnoreCase) ||
            !request.CompletedAt.HasValue ||
            request.CompletedAt.Value != completedAt)
        {
            throw new InvalidOperationException(
                "A completed interpretation request is required before recording interpreter work.");
        }

        if (_context.InterpreterWorkItems.Local.Any(item =>
                item.TenantId == _tenant.TenantId &&
                item.InterpretationRequestId == request.Id) ||
            await _context.InterpreterWorkItems.AnyAsync(item =>
                item.TenantId == _tenant.TenantId &&
                item.InterpretationRequestId == request.Id,
                cancellationToken))
        {
            return;
        }

        var tenant = await _context.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == _tenant.TenantId, cancellationToken);
        if (tenant == null)
            throw new InvalidOperationException("Tenant was not found while recording completed interpretation work.");

        var originalModality = request.ImagingStudy.Modality;
        var payrollModality = NormalizePayrollModality(originalModality);
        var rate = payrollModality == null
            ? null
            : await _context.InterpreterRates.FirstOrDefaultAsync(item =>
                item.TenantId == _tenant.TenantId &&
                item.InterpreterUserId == request.AssignedInterpreterId &&
                item.Modality == payrollModality &&
                item.IsActive,
                cancellationToken);

        _context.InterpreterWorkItems.Add(new InterpreterWorkItem
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            InterpretationRequestId = request.Id,
            ImagingOrderId = request.ImagingOrderId,
            ImagingStudyId = request.ImagingStudyId,
            InterpreterUserId = request.AssignedInterpreterId,
            CompletedAt = completedAt,
            OriginalModality = originalModality,
            PayrollModality = payrollModality,
            AccessionNumber = string.IsNullOrWhiteSpace(request.ImagingOrder.AccessionNumber)
                ? request.ImagingStudy.AccessionNumber
                : request.ImagingOrder.AccessionNumber,
            RateAmount = rate?.Amount,
            Currency = rate?.Currency ?? tenant.Currency,
            CreatedAt = DateTime.UtcNow,
        });
    }

    public async Task<InterpreterWorkSummaryResponse?> GetSummaryAsync(
        Guid interpreterUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty ||
            fromUtc >= toUtc ||
            !await _context.Users.AsNoTracking().AnyAsync(user =>
                user.Id == interpreterUserId &&
                user.TenantId == _tenant.TenantId &&
                user.Role == "Interpreter",
                cancellationToken))
        {
            return null;
        }

        var storedItems = await _context.InterpreterWorkItems
            .AsNoTracking()
            .Where(item =>
                item.TenantId == _tenant.TenantId &&
                item.InterpreterUserId == interpreterUserId &&
                item.CompletedAt >= fromUtc.UtcDateTime &&
                item.CompletedAt < toUtc.UtcDateTime)
            .OrderBy(item => item.CompletedAt)
            .ToListAsync(cancellationToken);

        var items = storedItems.Select(item => new InterpreterWorkItemResponse(
                item.Id,
                item.InterpretationRequestId,
                item.ImagingOrderId,
                item.ImagingStudyId,
                StaffWorkSessionService.ToUtcOffset(item.CompletedAt),
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

        return new InterpreterWorkSummaryResponse(
            interpreterUserId,
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

    public static string NormalizeRateModality(string modality) =>
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
