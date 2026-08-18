using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging")]
public class ImagingWorklistController : ControllerBase
{
    private const int MaxResults = 200;
    private static readonly HashSet<string> AllowedModalities = new(StringComparer.Ordinal)
    {
        "US",
        "DX"
    };

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ImagingWorklistController(AppDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet("worklist")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.ImagingGatewayScheme, Policy = PlatformAuthConstants.PolicyImagingGateway)]
    public async Task<IActionResult> GetWorklist(
        [FromQuery] string? modality,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved.");

        var normalizedModality = NormalizeModality(modality);
        if (normalizedModality == null && !string.IsNullOrWhiteSpace(modality))
            return BadRequest("modality must be one of: US, DX.");

        var fromUtc = from?.UtcDateTime;
        var toUtc = to?.UtcDateTime;

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

        if (fromUtc.HasValue)
        {
            query = query.Where(io => io.ScheduledStartTime >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(io => io.ScheduledStartTime <= toUtc.Value);
        }

        var items = await query
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
