using System.Security.Claims;
using Clienta.Api.DTOs;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/payment-confirmations")]
[Authorize(Policy = "manage_invoices")]
public sealed class PaymentConfirmationsController : ControllerBase
{
    private readonly PaymentConfirmationService _service;
    private readonly IPaymentConfirmationAccessService _access;
    private readonly ITenantContext _tenantContext;

    public PaymentConfirmationsController(
        PaymentConfirmationService service,
        IPaymentConfirmationAccessService access,
        ITenantContext tenantContext)
    {
        _service = service;
        _access = access;
        _tenantContext = tenantContext;
    }

    [HttpPost("invoices/{invoiceId:guid}")]
    public async Task<IActionResult> Create(Guid invoiceId, [FromBody] CreatePaymentConfirmationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(cancellationToken)) return Forbid();
        try
        {
            var result = await _service.CreateAsync(_tenantContext.TenantId, CurrentUserId(), invoiceId, request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(cancellationToken)) return Forbid();
        var result = await _service.GetAsync(_tenantContext.TenantId, id, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("invoice/{invoiceId:guid}")]
    public async Task<IActionResult> GetByInvoice(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(cancellationToken)) return Forbid();
        var result = await _service.GetByInvoiceAsync(_tenantContext.TenantId, invoiceId, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/proof")]
    public async Task<IActionResult> GetProof(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(cancellationToken)) return Forbid();
        var proof = await _service.OpenProofAsync(_tenantContext.TenantId, id, cancellationToken);
        if (proof == null) return NotFound();
        Response.RegisterForDispose(proof);
        Response.Headers.CacheControl = "no-store";
        return File(proof.Stream, proof.ContentType);
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(cancellationToken)) return Forbid();
        try { return await _service.RevokeAsync(_tenantContext.TenantId, id, cancellationToken) ? NoContent() : NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(cancellationToken)) return Forbid();
        try { return await _service.VerifyAsync(_tenantContext.TenantId, id, CurrentUserIdRequired(), cancellationToken) ? NoContent() : NotFound(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectPaymentConfirmationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(cancellationToken)) return Forbid();
        try { return await _service.RejectAsync(_tenantContext.TenantId, id, CurrentUserIdRequired(), request.Reason, cancellationToken) ? NoContent() : NotFound(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    private Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        _access.IsEnabledAsync(_tenantContext.TenantId, cancellationToken);

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;

    private Guid CurrentUserIdRequired() => CurrentUserId() ?? throw new UnauthorizedAccessException();
}