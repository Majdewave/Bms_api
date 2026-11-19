using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IWorkSummaryOverviewService
{
    Task<WorkSummaryOverviewResponse> GetOverviewAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}

public sealed class WorkSummaryOverviewService : IWorkSummaryOverviewService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;

    public WorkSummaryOverviewService(AppDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<WorkSummaryOverviewResponse> GetOverviewAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var from = fromUtc.UtcDateTime;
        var to = toUtc.UtcDateTime;
        var users = await _context.Users
            .AsNoTracking()
            .Where(user =>
                user.TenantId == _tenant.TenantId &&
                (user.Role == "Staff" || user.Role == "Interpreter" || user.Role == "Technician"))
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .Select(user => new
            {
                user.Id,
                FullName = user.FullName ?? user.Email,
                user.Role,
            })
            .ToListAsync(cancellationToken);

        var staffIds = users.Where(user => user.Role == "Staff").Select(user => user.Id).ToArray();
        var interpreterIds = users.Where(user => user.Role == "Interpreter").Select(user => user.Id).ToArray();
        var technicianIds = users.Where(user => user.Role == "Technician").Select(user => user.Id).ToArray();

        var sessions = staffIds.Length == 0
            ? new List<SessionPeriodData>()
            : await _context.StaffWorkSessions
                .AsNoTracking()
                .Where(session =>
                    session.TenantId == _tenant.TenantId &&
                    staffIds.Contains(session.UserId) &&
                    session.ClockOutAt.HasValue &&
                    session.ClockInAt < to &&
                    session.ClockOutAt.Value > from)
                .Select(session => new SessionPeriodData(
                    session.UserId,
                    session.ClockInAt,
                    session.ClockOutAt!.Value))
                .ToListAsync(cancellationToken);

        var openSessions = staffIds.Length == 0
            ? new List<OpenSessionData>()
            : await _context.StaffWorkSessions
                .AsNoTracking()
                .Where(session =>
                    session.TenantId == _tenant.TenantId &&
                    staffIds.Contains(session.UserId) &&
                    session.ClockOutAt == null)
                .Select(session => new OpenSessionData(session.UserId, session.ClockInAt))
                .ToListAsync(cancellationToken);

        var workItems = interpreterIds.Length == 0
            ? new List<WorkItemPeriodData>()
            : await _context.InterpreterWorkItems
                .AsNoTracking()
                .Where(item =>
                    item.TenantId == _tenant.TenantId &&
                    interpreterIds.Contains(item.InterpreterUserId) &&
                    item.CompletedAt >= from &&
                    item.CompletedAt < to)
                .Select(item => new WorkItemPeriodData(
                    item.InterpreterUserId,
                    item.PayrollModality,
                    item.RateAmount,
                    item.Currency))
                .ToListAsync(cancellationToken);

        var technicianWorkItems = technicianIds.Length == 0
            ? new List<TechnicianWorkItemPeriodData>()
            : await _context.TechnicianWorkItems
                .AsNoTracking()
                .Where(item =>
                    item.TenantId == _tenant.TenantId &&
                    technicianIds.Contains(item.TechnicianUserId) &&
                    item.PerformedAt >= from &&
                    item.PerformedAt < to)
                .Select(item => new TechnicianWorkItemPeriodData(
                    item.TechnicianUserId,
                    item.PayrollModality,
                    item.RateAmount,
                    item.Currency))
                .ToListAsync(cancellationToken);

        var sessionsByUser = sessions
            .GroupBy(session => session.UserId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var openSessionByUser = openSessions
            .GroupBy(session => session.UserId)
            .ToDictionary(group => group.Key, group => group.First());
        var workItemsByUser = workItems
            .GroupBy(item => item.InterpreterUserId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var technicianWorkItemsByUser = technicianWorkItems
            .GroupBy(item => item.TechnicianUserId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var members = users.Select(user =>
        {
            if (user.Role == "Staff")
            {
                var userSessions = sessionsByUser.TryGetValue(user.Id, out var foundSessions)
                    ? foundSessions
                    : Array.Empty<SessionPeriodData>();
                var intervals = userSessions.Select(session =>
                {
                    var intervalStart = session.ClockInAt > from ? session.ClockInAt : from;
                    var intervalEnd = session.ClockOutAt < to ? session.ClockOutAt : to;
                    return new
                    {
                        Minutes = Math.Max(0, (long)Math.Floor((intervalEnd - intervalStart).TotalMinutes)),
                    };
                }).ToArray();
                var workingDays = userSessions
                    .Select(session => DateOnly.FromDateTime(session.ClockInAt > from ? session.ClockInAt : from))
                    .Distinct()
                    .Count();
                var openSession = openSessionByUser.TryGetValue(user.Id, out var foundOpenSession)
                    ? foundOpenSession
                    : null;

                return new WorkSummaryOverviewMemberResponse(
                    user.Id,
                    user.FullName,
                    user.Role,
                    workingDays,
                    userSessions.Length,
                    intervals.Sum(interval => interval.Minutes),
                    openSession != null,
                    openSession == null ? null : StaffWorkSessionService.ToUtcOffset(openSession.ClockInAt),
                    null,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<InterpreterPayableTotalResponse>());
            }

            if (user.Role == "Interpreter")
            {
                var userWorkItems = workItemsByUser.TryGetValue(user.Id, out var foundWorkItems)
                    ? foundWorkItems
                    : Array.Empty<WorkItemPeriodData>();
                var withRate = userWorkItems.Where(item => item.RateAmount.HasValue).ToArray();
                return new WorkSummaryOverviewMemberResponse(
                    user.Id,
                    user.FullName,
                    user.Role,
                    null,
                    null,
                    null,
                    null,
                    null,
                    userWorkItems.Length,
                    withRate.Length,
                    userWorkItems.Length - withRate.Length,
                    userWorkItems.Count(item => item.PayrollModality == "XRay"),
                    userWorkItems.Count(item => item.PayrollModality == "US"),
                    GetPayableTotals(withRate.Select(item => (item.RateAmount, item.Currency))));
            }

            var userTechnicianWorkItems = technicianWorkItemsByUser.TryGetValue(user.Id, out var foundTechnicianWorkItems)
                ? foundTechnicianWorkItems
                : Array.Empty<TechnicianWorkItemPeriodData>();
            var technicianItemsWithRate = userTechnicianWorkItems.Where(item => item.RateAmount.HasValue).ToArray();
            return new WorkSummaryOverviewMemberResponse(
                user.Id,
                user.FullName,
                user.Role,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                userTechnicianWorkItems.Count(item => item.PayrollModality == "XRay"),
                userTechnicianWorkItems.Count(item => item.PayrollModality == "US"),
                GetPayableTotals(technicianItemsWithRate.Select(item => (item.RateAmount, item.Currency))),
                userTechnicianWorkItems.Length,
                technicianItemsWithRate.Length,
                userTechnicianWorkItems.Length - technicianItemsWithRate.Length);
        }).ToArray();

        var interpreterWork = workItems.ToArray();
        var totalWithRate = interpreterWork.Where(item => item.RateAmount.HasValue).ToArray();
        return new WorkSummaryOverviewResponse(
            fromUtc.ToUniversalTime(),
            toUtc.ToUniversalTime(),
            members.Length,
            members.Where(member => member.Role == "Staff").Sum(member => member.WorkingDays ?? 0),
            members.Where(member => member.Role == "Staff").Sum(member => member.CompletedSessions ?? 0),
            members.Where(member => member.Role == "Staff").Sum(member => member.TotalWorkedMinutes ?? 0),
            interpreterWork.Length,
            totalWithRate.Length,
            interpreterWork.Length - totalWithRate.Length,
            GetPayableTotals(totalWithRate.Select(item => (item.RateAmount, item.Currency))),
            members);
    }

    private static IReadOnlyList<InterpreterPayableTotalResponse> GetPayableTotals(
        IEnumerable<(decimal? RateAmount, string? Currency)> items) =>
        items
            .Where(item => item.RateAmount.HasValue)
            .GroupBy(item => item.Currency ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .Select(group => new InterpreterPayableTotalResponse(
                group.Key,
                group.Sum(item => item.RateAmount!.Value)))
            .ToArray();

    private sealed record SessionPeriodData(Guid UserId, DateTime ClockInAt, DateTime ClockOutAt);
    private sealed record OpenSessionData(Guid UserId, DateTime ClockInAt);
    private sealed record WorkItemPeriodData(Guid InterpreterUserId, string? PayrollModality, decimal? RateAmount, string? Currency);
    private sealed record TechnicianWorkItemPeriodData(Guid TechnicianUserId, string? PayrollModality, decimal? RateAmount, string? Currency);
}
