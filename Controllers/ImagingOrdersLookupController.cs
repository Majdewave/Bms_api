using Clienta.Api.Authorization;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/imaging/orders")]
public class ImagingOrdersLookupController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ImagingOrdersLookupController(AppDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet("by-accession/{accessionNumber}")]
    [Authorize(AuthenticationSchemes = PlatformAuthConstants.ImagingGatewayScheme, Policy = PlatformAuthConstants.PolicyImagingGateway)]
    public async Task<IActionResult> GetByAccession(string accessionNumber, CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Unauthorized("Tenant not resolved.");

        if (string.IsNullOrWhiteSpace(accessionNumber))
            return BadRequest("accessionNumber is required.");

        var normalizedAccession = accessionNumber.Trim().ToUpperInvariant();

        var order = await _db.ImagingOrders
            .AsNoTracking()
            .Where(io => io.TenantId == _tenantContext.TenantId)
            .Where(io => io.AccessionNumber == normalizedAccession)
            .Select(io => new ImagingOrderByAccessionDto(
                io.Id,
                io.AccessionNumber,
                io.Modality,
                io.Status,
                io.ClientId,
                io.AppointmentId,
                io.ServiceId
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (order == null)
            return NotFound();

        return Ok(order);
    }
}
