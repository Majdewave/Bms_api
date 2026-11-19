using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/work-summary")]
public sealed class WorkSummaryController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IAuthorizationService _authorization;
    private readonly IStaffWorkSessionService _staffSessions;
    private readonly IInterpreterWorkSummaryService _interpreterWork;
    private readonly ITechnicianWorkSummaryService _technicianWork;
    private readonly IWorkSummaryOverviewService _overview;

    public WorkSummaryController(
        AppDbContext context,
        ITenantContext tenant,
        IAuthorizationService authorization,
        IStaffWorkSessionService staffSessions,
        IInterpreterWorkSummaryService interpreterWork,
        ITechnicianWorkSummaryService technicianWork,
        IWorkSummaryOverviewService overview)
    {
        _context = context;
        _tenant = tenant;
        _authorization = authorization;
        _staffSessions = staffSessions;
        _interpreterWork = interpreterWork;
        _technicianWork = technicianWork;
        _overview = overview;
    }

    /// <summary>Returns the tenant's staff and interpreter totals for a selected UTC range.</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(WorkSummaryOverviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        if (!fromUtc.HasValue || !toUtc.HasValue || fromUtc >= toUtc)
            return BadRequest(new { error = "valid_utc_range_required" });

        return Ok(await _overview.GetOverviewAsync(fromUtc.Value, toUtc.Value, cancellationToken));
    }

    [HttpGet("attendance/current")]
    public async Task<IActionResult> GetAttendanceStatus(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserStaffAsync(cancellationToken))
            return Forbid();

        var session = await _staffSessions.GetOpenSessionAsync(cancellationToken);
        long? elapsedMinutes = session == null
            ? null
            : Math.Max(0, (long)Math.Floor((DateTime.UtcNow - session.ClockInAt).TotalMinutes));

        return Ok(new CurrentStaffAttendanceStatusResponse(
            session != null,
            session?.Id,
            session == null ? null : StaffWorkSessionService.ToUtcOffset(session.ClockInAt),
            elapsedMinutes));
    }

    [HttpPost("attendance/clock-in")]
    public async Task<IActionResult> ClockIn(CancellationToken cancellationToken)
    {
        var outcome = await _staffSessions.ClockInAsync(cancellationToken);
        if (!outcome.IsEligible)
            return Forbid();
        if (outcome.ExistingSession != null)
            return Conflict(new { error = "staff_already_clocked_in", sessionId = outcome.ExistingSession.Id });

        return CreatedAtAction(
            nameof(GetAttendanceStatus),
            new { },
            ToSessionResponse(outcome.NewSession!));
    }

    [HttpPost("attendance/clock-out")]
    public async Task<IActionResult> ClockOut(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserStaffAsync(cancellationToken))
            return Forbid();

        var session = await _staffSessions.ClockOutAsync(cancellationToken);
        return session == null
            ? Conflict(new { error = "staff_not_clocked_in" })
            : Ok(ToSessionResponse(session));
    }

    [HttpPut("staff/{staffUserId:guid}/sessions/{sessionId:guid}")]
    public async Task<IActionResult> CorrectSession(
        Guid staffUserId,
        Guid sessionId,
        CorrectStaffWorkSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Reason) ||
            request.ClockInAt == default ||
            request.ClockInAt > DateTimeOffset.UtcNow ||
            (request.ClockOutAt.HasValue &&
             (request.ClockOutAt.Value <= request.ClockInAt || request.ClockOutAt.Value > DateTimeOffset.UtcNow)))
        {
            return BadRequest(new { error = "invalid_staff_session_correction" });
        }

        var sessionOwnerMatches = await _context.StaffWorkSessions
            .AsNoTracking()
            .AnyAsync(session =>
                session.Id == sessionId &&
                session.TenantId == _tenant.TenantId &&
                session.UserId == staffUserId,
                cancellationToken);
        if (!sessionOwnerMatches)
            return NotFound();

        var outcome = await _staffSessions.CorrectAsync(
            sessionId,
            request.ClockInAt,
            request.ClockOutAt,
            request.Reason,
            cancellationToken);
        if (outcome.HasConflictingOpenSession)
            return Conflict(new { error = "staff_already_has_open_session" });
        return outcome.Session == null ? NotFound() : Ok(ToSessionResponse(outcome.Session));
    }

    [HttpGet("staff/{staffUserId:guid}/hours")]
    public async Task<IActionResult> GetStaffHours(
        Guid staffUserId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        if (!fromUtc.HasValue || !toUtc.HasValue || fromUtc >= toUtc)
            return BadRequest(new { error = "valid_utc_range_required" });

        var summary = await _staffSessions.GetHoursSummaryAsync(
            staffUserId,
            fromUtc.Value,
            toUtc.Value,
            cancellationToken);
        return summary == null ? NotFound() : Ok(summary);
    }

    [HttpGet("interpreter-rates")]
    public async Task<IActionResult> GetInterpreterRates(CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        return Ok(await _interpreterWork.GetRatesAsync(cancellationToken));
    }

    [HttpPut("interpreters/{interpreterUserId:guid}/rates/{modality}")]
    public async Task<IActionResult> SetInterpreterRate(
        Guid interpreterUserId,
        string modality,
        SetInterpreterRateRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        if (!IsSupportedPayrollModality(modality))
            return BadRequest(new { error = "unsupported_payroll_modality" });

        var rate = await _interpreterWork.SetRateAsync(
            interpreterUserId,
            modality,
            request.Amount,
            cancellationToken);
        return rate == null ? NotFound() : Ok(rate);
    }

    [HttpDelete("interpreter-rates/{rateId:guid}")]
    public async Task<IActionResult> DeactivateInterpreterRate(
        Guid rateId,
        CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        return await _interpreterWork.DeactivateRateAsync(rateId, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpGet("interpreters/{interpreterUserId:guid}/work")]
    public async Task<IActionResult> GetInterpreterWork(
        Guid interpreterUserId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        if (!fromUtc.HasValue || !toUtc.HasValue || fromUtc >= toUtc)
            return BadRequest(new { error = "valid_utc_range_required" });

        var summary = await _interpreterWork.GetSummaryAsync(
            interpreterUserId,
            fromUtc.Value,
            toUtc.Value,
            cancellationToken);
        return summary == null ? NotFound() : Ok(summary);
    }

    [HttpGet("technician-rates")]
    public async Task<IActionResult> GetTechnicianRates(CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        return Ok(await _technicianWork.GetRatesAsync(cancellationToken));
    }

    [HttpPut("technicians/{technicianUserId:guid}/rates/{modality}")]
    public async Task<IActionResult> SetTechnicianRate(
        Guid technicianUserId,
        string modality,
        SetTechnicianRateRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        if (!IsSupportedPayrollModality(modality))
            return BadRequest(new { error = "unsupported_payroll_modality" });

        var rate = await _technicianWork.SetRateAsync(
            technicianUserId,
            modality,
            request.Amount,
            cancellationToken);
        return rate == null ? NotFound() : Ok(rate);
    }

    [HttpDelete("technician-rates/{rateId:guid}")]
    public async Task<IActionResult> DeactivateTechnicianRate(
        Guid rateId,
        CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        return await _technicianWork.DeactivateRateAsync(rateId, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpGet("technicians/{technicianUserId:guid}/work")]
    public async Task<IActionResult> GetTechnicianWork(
        Guid technicianUserId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        if (!await CanManageStaffAsync(cancellationToken))
            return Forbid();
        if (!fromUtc.HasValue || !toUtc.HasValue || fromUtc >= toUtc)
            return BadRequest(new { error = "valid_utc_range_required" });

        var summary = await _technicianWork.GetSummaryAsync(
            technicianUserId,
            fromUtc.Value,
            toUtc.Value,
            cancellationToken);
        return summary == null ? NotFound() : Ok(summary);
    }

    private async Task<bool> IsCurrentUserStaffAsync(CancellationToken cancellationToken)
    {
        return _tenant.TenantId != Guid.Empty &&
            _tenant.UserId.HasValue &&
            User.IsInRole("Staff") &&
            await _context.Users.AsNoTracking().AnyAsync(user =>
                user.Id == _tenant.UserId.Value &&
                user.TenantId == _tenant.TenantId &&
                user.Role == "Staff" &&
                user.IsActive,
                cancellationToken);
    }

    private async Task<bool> CanManageStaffAsync(CancellationToken cancellationToken)
    {
        var isBusinessTenant = await _context.Tenants.AsNoTracking()
            .AnyAsync(tenant =>
                tenant.Id == _tenant.TenantId &&
                tenant.ProductType == TenantProductType.Business,
                cancellationToken);
        var hasManageStaffPermission = (await _authorization.AuthorizeAsync(
            User,
            resource: null,
            policyName: "manage_staff")).Succeeded;
        return StaffManagementAccessRules.CanManage(
            isBusinessTenant,
            User.IsInRole("Admin"),
            hasManageStaffPermission);
    }

    private static bool IsSupportedPayrollModality(string modality) =>
        modality.Equals("XRay", StringComparison.OrdinalIgnoreCase) ||
        modality.Equals("US", StringComparison.OrdinalIgnoreCase);

    private static StaffWorkSessionResponse ToSessionResponse(StaffWorkSession session) => new(
        session.Id,
        session.UserId,
        StaffWorkSessionService.ToUtcOffset(session.ClockInAt),
        session.ClockOutAt.HasValue ? StaffWorkSessionService.ToUtcOffset(session.ClockOutAt.Value) : null,
        StaffWorkSessionService.ToUtcOffset(session.CreatedAt),
        StaffWorkSessionService.ToUtcOffset(session.UpdatedAt),
        session.LastCorrectedByUserId,
        session.LastCorrectedAt.HasValue ? StaffWorkSessionService.ToUtcOffset(session.LastCorrectedAt.Value) : null,
        session.CorrectionReason);
}
