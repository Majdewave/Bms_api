using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging")]
public class ImagingWorklistController : ControllerBase
{
    private const int MaxResults = 200;
    private static readonly HashSet<string> AllowedModalities = new(StringComparer.Ordinal)
    {
        "US",
        "DX",
        "CR"
    };

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IDepartmentFeatureResolver _departmentFeatureResolver;

    public ImagingWorklistController(
        AppDbContext db,
        ITenantContext tenantContext,
        IDepartmentFeatureResolver departmentFeatureResolver)
    {
        _db = db;
        _tenantContext = tenantContext;
        _departmentFeatureResolver = departmentFeatureResolver;
    }

    [HttpGet("worklist")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.ImagingGatewayScheme, Policy = PlatformAuthConstants.PolicyImagingGateway)]
    public async Task<IActionResult> GetWorklist(
        [FromQuery] string? modality,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? localDate,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved.");

        var normalizedModality = NormalizeModality(modality);
        if (normalizedModality == null && !string.IsNullOrWhiteSpace(modality))
            return BadRequest("modality must be one of: US, DX, CR.");

        var fromUtc = from?.UtcDateTime;
        var toUtc = to?.UtcDateTime;
        DateTime? localDayStart = null;
        DateTime? localDayEndExclusive = null;

        if (!string.IsNullOrWhiteSpace(localDate))
        {
            if (fromUtc.HasValue || toUtc.HasValue)
                return BadRequest("localDate cannot be combined with from or to.");

            if (!DateOnly.TryParseExact(localDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedLocalDate) ||
                parsedLocalDate == DateOnly.MaxValue)
            {
                return BadRequest("localDate must be a valid date in yyyy-MM-dd format.");
            }

            // MWL localDate is a legacy clinic-wall-calendar date, not an instant.
            // ScheduledStartTime is PostgreSQL timestamptz, so mark these unchanged
            // wall-clock digits as UTC for Npgsql compatibility; do not convert them
            // through the clinic time zone.
            localDayStart = parsedLocalDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            localDayEndExclusive = parsedLocalDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
            return BadRequest("from must be less than or equal to to.");

        var query = _db.ImagingOrders
            .AsNoTracking()
            .Where(io => io.TenantId == _tenantContext.TenantId)
            .Where(io => io.Status == ImagingOrderStatuses.Scheduled);

        if (!string.IsNullOrEmpty(normalizedModality))
        {
            query = query.Where(io => io.Modality == normalizedModality);
        }

        if (localDayStart.HasValue && localDayEndExclusive.HasValue)
        {
            query = query.Where(io =>
                io.ScheduledStartTime >= localDayStart.Value &&
                io.ScheduledStartTime < localDayEndExclusive.Value);
        }
        else if (fromUtc.HasValue)
        {
            query = query.Where(io => io.ScheduledStartTime >= fromUtc.Value);
        }

        if (!localDayEndExclusive.HasValue && toUtc.HasValue)
        {
            query = query.Where(io => io.ScheduledStartTime <= toUtc.Value);
        }

        var candidateOrders = await query
            .OrderBy(io => io.ScheduledStartTime)
            .Take(MaxResults)
            .Select(io => new
            {
                io.Id,
                DepartmentId = io.Appointment != null
                    ? io.Appointment.DepartmentId
                    : null
            })
            .ToListAsync(cancellationToken);

        var enabledOrderIds = new HashSet<Guid>();

        Console.WriteLine(
            $"MWL DEBUG: Tenant={_tenantContext.TenantId}, Modality={normalizedModality}, LocalDate={localDate}, CandidateCount={candidateOrders.Count}");

        foreach (var candidate in candidateOrders)
        {
            var enabled = await _departmentFeatureResolver.IsFeatureEnabledAsync(
                _tenantContext.TenantId,
                candidate.DepartmentId,
                "medicalImagingEnabled",
                cancellationToken);

            Console.WriteLine(
                $"MWL DEBUG: OrderId={candidate.Id}, DepartmentId={candidate.DepartmentId}, MedicalImagingEnabled={enabled}");

            if (enabled)
            {
                enabledOrderIds.Add(candidate.Id);
            }
        }

        var items = await query
            .Where(io => enabledOrderIds.Contains(io.Id))
            .OrderBy(io => io.ScheduledStartTime)
            .Take(MaxResults)
            .Select(io => new ImagingWorklistItemDto(
                io.Id,
                io.AccessionNumber,
                io.Modality,
                io.ScheduledStartTime,
                io.Status,
                io.ClientId,
                io.Client.FullName,
                io.Client.IdNumber,
                io.Client.BirthDate,
                io.Client.Phone,
                io.AppointmentId,
                io.ServiceId,
                io.Service != null ? io.Service.Name : null
            ))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    private static string? NormalizeModality(string? modality)
    {
        if (string.IsNullOrWhiteSpace(modality))
            return null;

        var normalized = modality.Trim().ToUpperInvariant();
        return AllowedModalities.Contains(normalized) ? normalized : null;
    }
}
