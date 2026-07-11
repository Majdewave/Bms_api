using Clienta.Api.DTOs;
using Clienta.Api.Services.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/whatsapp")]
[Authorize(Policy = "manage_whatsapp")]
public class WhatsAppController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;

    public WhatsAppController(IWhatsAppService whatsAppService)
    {
        _whatsAppService = whatsAppService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _whatsAppService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    [HttpGet("test-connection")]
    public async Task<IActionResult> TestConnection(CancellationToken cancellationToken)
    {
        var diagnostics = await _whatsAppService.TestConnectionAsync(cancellationToken);
        return Ok(diagnostics);
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect(CancellationToken cancellationToken)
    {
        var payload = await _whatsAppService.InitializeConnectAsync(cancellationToken);
        return Ok(payload);
    }

    [HttpPost("complete")]
    public async Task<IActionResult> CompleteEmbeddedSignup([FromBody] WhatsAppEmbeddedSignupCompleteRequest request, CancellationToken cancellationToken)
    {
        await _whatsAppService.CompleteEmbeddedSignupAsync(request, cancellationToken);
        return Ok(new { message = "WhatsApp connected successfully." });
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken)
    {
        var callbackHost = Request.Host.HasValue ? Request.Host.Value : null;
        var result = await _whatsAppService.HandleCallbackAsync(code, state, error, errorDescription, callbackHost, cancellationToken);
        return Redirect(result.RedirectUrl);
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await _whatsAppService.DisconnectAsync(cancellationToken);
        return Ok(new { message = "WhatsApp disconnected." });
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates(CancellationToken cancellationToken)
    {
        var templates = await _whatsAppService.GetTemplatesAsync(cancellationToken);
        return Ok(templates);
    }

    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages(CancellationToken cancellationToken)
    {
        var messages = await _whatsAppService.GetMessagesAsync(cancellationToken);
        return Ok(messages);
    }
}
