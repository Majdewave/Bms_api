using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.DTOs;

/// <summary>Current clock status for the authenticated staff member.</summary>
public sealed record CurrentStaffAttendanceStatusResponse(
    bool IsClockedIn,
    Guid? OpenSessionId,
    DateTimeOffset? ClockInAt,
    long? ElapsedMinutes);

/// <summary>Represents a recorded staff work session.</summary>
public sealed record StaffWorkSessionResponse(
    Guid Id,
    Guid UserId,
    DateTimeOffset ClockInAt,
    DateTimeOffset? ClockOutAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? LastCorrectedByUserId,
    DateTimeOffset? LastCorrectedAt,
    string? CorrectionReason);

/// <summary>Manager correction to the UTC start and optional end of a work session.</summary>
public sealed record CorrectStaffWorkSessionRequest
{
    /// <summary>Corrected UTC clock-in instant.</summary>
    [Required]
    public DateTimeOffset ClockInAt { get; init; }

    /// <summary>Corrected UTC clock-out instant, or null to reopen the session.</summary>
    public DateTimeOffset? ClockOutAt { get; init; }

    /// <summary>Required audit reason for the correction.</summary>
    [Required, MaxLength(500)]
    public required string Reason { get; init; }
}

/// <summary>One completed session intersecting a requested UTC date range.</summary>
public sealed record StaffWorkSessionSummaryItemResponse(
    Guid SessionId,
    DateOnly SessionDate,
    DateTimeOffset ClockInAt,
    DateTimeOffset ClockOutAt,
    long WorkedMinutesInRange);

/// <summary>Completed work-session details and totals for a staff member.</summary>
public sealed record StaffHoursSummaryResponse(
    Guid StaffUserId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<StaffWorkSessionSummaryItemResponse> Sessions,
    int CompletedSessions,
    int WorkingDays,
    long TotalWorkedMinutes);

/// <summary>Request to set the current rate for one interpreter payroll modality.</summary>
public sealed record SetInterpreterRateRequest
{
    /// <summary>Nonnegative per-interpretation rate in the tenant currency.</summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Amount { get; init; }
}

/// <summary>Request to set the current per-examination rate for one technician payroll modality.</summary>
public sealed record SetTechnicianRateRequest
{
    /// <summary>Nonnegative per-examination rate in the tenant currency.</summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Amount { get; init; }
}

/// <summary>Current or inactive historical interpreter rate configuration.</summary>
public sealed record InterpreterRateResponse(
    Guid Id,
    Guid InterpreterUserId,
    string InterpreterName,
    string Modality,
    decimal Amount,
    string Currency,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One durable completed interpretation payroll item.</summary>
public sealed record InterpreterWorkItemResponse(
    Guid Id,
    Guid InterpretationRequestId,
    Guid ImagingOrderId,
    Guid ImagingStudyId,
    DateTimeOffset CompletedAt,
    string AccessionNumber,
    string OriginalModality,
    string? PayrollModality,
    decimal? RateAmount,
    string? Currency);

/// <summary>Interpretation count grouped by normalized payroll modality.</summary>
public sealed record InterpreterModalityCountResponse(string? PayrollModality, int Count);

/// <summary>Payable total for a single snapshotted currency.</summary>
public sealed record InterpreterPayableTotalResponse(string Currency, decimal Amount);

/// <summary>Interpreter work and historical rate totals for a UTC date range.</summary>
public sealed record InterpreterWorkSummaryResponse(
    Guid InterpreterUserId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<InterpreterWorkItemResponse> Items,
    int TotalInterpretations,
    int InterpretationsWithRate,
    int InterpretationsWithoutRate,
    IReadOnlyList<InterpreterModalityCountResponse> CountsByPayrollModality,
    IReadOnlyList<InterpreterPayableTotalResponse> PayableByCurrency);

/// <summary>Current or inactive historical technician rate configuration.</summary>
public sealed record TechnicianRateResponse(
    Guid Id,
    Guid TechnicianUserId,
    string TechnicianName,
    string Modality,
    decimal Amount,
    string Currency,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One durable technician work item for a received imaging study.</summary>
public sealed record TechnicianWorkItemResponse(
    Guid Id,
    Guid ImagingOrderId,
    Guid ImagingStudyId,
    DateTimeOffset PerformedAt,
    string AccessionNumber,
    string OriginalModality,
    string? PayrollModality,
    decimal? RateAmount,
    string? Currency);

/// <summary>Technician examination totals and historical rate snapshots for a UTC date range.</summary>
public sealed record TechnicianWorkSummaryResponse(
    Guid TechnicianUserId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<TechnicianWorkItemResponse> Items,
    int TotalExaminations,
    int ExaminationsWithRate,
    int ExaminationsWithoutRate,
    IReadOnlyList<InterpreterModalityCountResponse> CountsByPayrollModality,
    IReadOnlyList<InterpreterPayableTotalResponse> PayableByCurrency);

/// <summary>Tenant-wide staff, interpreter, and technician totals for a selected UTC period.</summary>
public sealed record WorkSummaryOverviewResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int MemberCount,
    int WorkingDays,
    int CompletedSessions,
    long TotalWorkedMinutes,
    int TotalInterpretations,
    int InterpretationsWithRate,
    int InterpretationsWithoutRate,
    IReadOnlyList<InterpreterPayableTotalResponse> PayableByCurrency,
    IReadOnlyList<WorkSummaryOverviewMemberResponse> Members);

/// <summary>Period totals and current attendance state for one staff member or interpreter.</summary>
public sealed record WorkSummaryOverviewMemberResponse(
    Guid UserId,
    string FullName,
    string Role,
    int? WorkingDays,
    int? CompletedSessions,
    long? TotalWorkedMinutes,
    bool? IsClockedIn,
    DateTimeOffset? ClockInAt,
    int? TotalInterpretations,
    int? InterpretationsWithRate,
    int? InterpretationsWithoutRate,
    int? XRayCount,
    int? USCount,
    IReadOnlyList<InterpreterPayableTotalResponse> PayableByCurrency,
    int? TotalExaminations = null,
    int? ExaminationsWithRate = null,
    int? ExaminationsWithoutRate = null);
