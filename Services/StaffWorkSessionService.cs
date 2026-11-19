using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public sealed record ClockInOutcome(bool IsEligible, StaffWorkSession? ExistingSession, StaffWorkSession? NewSession);
public sealed record StaffSessionCorrectionOutcome(StaffWorkSession? Session, bool HasConflictingOpenSession);

public interface IStaffWorkSessionService
{
    Task<ClockInOutcome> ClockInAsync(CancellationToken cancellationToken);
    Task<StaffWorkSession?> ClockOutAsync(CancellationToken cancellationToken);
    Task<StaffWorkSession?> GetOpenSessionAsync(CancellationToken cancellationToken);
    Task<StaffSessionCorrectionOutcome> CorrectAsync(
        Guid sessionId,
        DateTimeOffset clockInAt,
        DateTimeOffset? clockOutAt,
        string reason,
        CancellationToken cancellationToken);
    Task<StaffHoursSummaryResponse?> GetHoursSummaryAsync(
        Guid staffUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}

public sealed class StaffWorkSessionService : IStaffWorkSessionService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;

    public StaffWorkSessionService(AppDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<ClockInOutcome> ClockInAsync(CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty || !_tenant.UserId.HasValue)
            return new ClockInOutcome(false, null, null);

        var userId = _tenant.UserId.Value;
        var isEligible = await _context.Users.AnyAsync(user =>
            user.Id == userId &&
            user.TenantId == _tenant.TenantId &&
            user.IsActive &&
            user.Role == "Staff",
            cancellationToken);

        if (!isEligible)
            return new ClockInOutcome(false, null, null);

        var existing = await _context.StaffWorkSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session =>
                session.TenantId == _tenant.TenantId &&
                session.UserId == userId &&
                session.ClockOutAt == null,
                cancellationToken);

        if (existing != null)
            return new ClockInOutcome(true, existing, null);

        var now = DateTime.UtcNow;
        var session = new StaffWorkSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            UserId = userId,
            ClockInAt = now,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.StaffWorkSessions.Add(session);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new ClockInOutcome(true, null, session);
        }
        catch (DbUpdateException)
        {
            _context.Entry(session).State = EntityState.Detached;
            var concurrentOpenSession = await _context.StaffWorkSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(existingSession =>
                    existingSession.TenantId == _tenant.TenantId &&
                    existingSession.UserId == userId &&
                    existingSession.ClockOutAt == null,
                    cancellationToken);
            if (concurrentOpenSession != null)
                return new ClockInOutcome(true, concurrentOpenSession, null);

            throw;
        }
    }

    public async Task<StaffWorkSession?> ClockOutAsync(CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty || !_tenant.UserId.HasValue)
            return null;

        var userId = _tenant.UserId.Value;
        var sessionId = await _context.StaffWorkSessions
            .Where(session =>
                session.TenantId == _tenant.TenantId &&
                session.UserId == userId &&
                session.ClockOutAt == null)
            .Select(session => (Guid?)session.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!sessionId.HasValue)
            return null;

        var now = DateTime.UtcNow;
        var updatedCount = await _context.StaffWorkSessions
            .Where(session =>
                session.Id == sessionId.Value &&
                session.TenantId == _tenant.TenantId &&
                session.UserId == userId &&
                session.ClockOutAt == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(session => session.ClockOutAt, now)
                .SetProperty(session => session.ClockOutByUserId, userId)
                .SetProperty(session => session.UpdatedAt, now),
                cancellationToken);

        if (updatedCount == 0)
            return null;

        return await _context.StaffWorkSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session =>
                session.Id == sessionId.Value &&
                session.TenantId == _tenant.TenantId &&
                session.UserId == userId,
                cancellationToken);
    }

    public Task<StaffWorkSession?> GetOpenSessionAsync(CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty || !_tenant.UserId.HasValue)
            return Task.FromResult<StaffWorkSession?>(null);

        var userId = _tenant.UserId.Value;
        return _context.StaffWorkSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session =>
                session.TenantId == _tenant.TenantId &&
                session.UserId == userId &&
                session.ClockOutAt == null,
                cancellationToken);
    }

    public async Task<StaffSessionCorrectionOutcome> CorrectAsync(
        Guid sessionId,
        DateTimeOffset clockInAt,
        DateTimeOffset? clockOutAt,
        string reason,
        CancellationToken cancellationToken)
    {
        var session = await _context.StaffWorkSessions.FirstOrDefaultAsync(
            item => item.Id == sessionId && item.TenantId == _tenant.TenantId,
            cancellationToken);
        if (session == null)
            return new StaffSessionCorrectionOutcome(null, false);

        if (!clockOutAt.HasValue)
        {
            var otherOpenSessionExists = await _context.StaffWorkSessions.AnyAsync(item =>
                item.TenantId == _tenant.TenantId &&
                item.UserId == session.UserId &&
                item.Id != session.Id &&
                item.ClockOutAt == null,
                cancellationToken);
            if (otherOpenSessionExists)
                return new StaffSessionCorrectionOutcome(null, true);
        }

        var now = DateTime.UtcNow;
        session.ClockInAt = clockInAt.UtcDateTime;
        session.ClockOutAt = clockOutAt?.UtcDateTime;
        session.ClockOutByUserId = clockOutAt.HasValue ? _tenant.UserId : null;
        session.LastCorrectedByUserId = _tenant.UserId;
        session.LastCorrectedAt = now;
        session.CorrectionReason = reason.Trim();
        session.UpdatedAt = now;
        await _context.SaveChangesAsync(cancellationToken);
        return new StaffSessionCorrectionOutcome(session, false);
    }

    public async Task<StaffHoursSummaryResponse?> GetHoursSummaryAsync(
        Guid staffUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        if (_tenant.TenantId == Guid.Empty ||
            fromUtc >= toUtc ||
            !await _context.Users.AsNoTracking().AnyAsync(user =>
                user.Id == staffUserId &&
                user.TenantId == _tenant.TenantId &&
                user.Role == "Staff",
                cancellationToken))
        {
            return null;
        }

        var from = fromUtc.UtcDateTime;
        var to = toUtc.UtcDateTime;
        var sessions = await _context.StaffWorkSessions
            .AsNoTracking()
            .Where(session =>
                session.TenantId == _tenant.TenantId &&
                session.UserId == staffUserId &&
                session.ClockOutAt.HasValue &&
                session.ClockInAt < to &&
                session.ClockOutAt.Value > from)
            .OrderBy(session => session.ClockInAt)
            .ToListAsync(cancellationToken);

        var items = sessions.Select(session =>
        {
            var intervalStart = session.ClockInAt > from ? session.ClockInAt : from;
            var clockOut = session.ClockOutAt!.Value;
            var intervalEnd = clockOut < to ? clockOut : to;
            var workedMinutes = Math.Max(0, (long)Math.Floor((intervalEnd - intervalStart).TotalMinutes));
            return new StaffWorkSessionSummaryItemResponse(
                session.Id,
                DateOnly.FromDateTime(session.ClockInAt),
                ToUtcOffset(session.ClockInAt),
                ToUtcOffset(clockOut),
                workedMinutes);
        }).ToArray();

        var workingDays = sessions
            .Select(session => DateOnly.FromDateTime(session.ClockInAt > from ? session.ClockInAt : from))
            .Distinct()
            .Count();

        return new StaffHoursSummaryResponse(
            staffUserId,
            fromUtc.ToUniversalTime(),
            toUtc.ToUniversalTime(),
            items,
            items.Length,
            workingDays,
            items.Sum(item => item.WorkedMinutesInRange));
    }

    internal static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
