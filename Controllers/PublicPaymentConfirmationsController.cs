using Clienta.Api.DTOs;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/public/payment-confirmations")]
[AllowAnonymous]
public sealed class PublicPaymentConfirmationsController : ControllerBase
{
    private readonly PaymentConfirmationService _service;

    public PublicPaymentConfirmationsController(PaymentConfirmationService service)
    {
        _service = service;
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken cancellationToken)
    {
        var result = await _service.GetPublicAsync(token, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{token}")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Submit(string token, [FromForm] SubmitPaymentConfirmationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.SubmitAsync(token, request, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (InvalidDataException ex) { return BadRequest(ex.Message); }
    }
}