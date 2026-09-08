using Clienta.Api.DTOs;
using Clienta.Api.Services.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Clienta.Api.Services;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/whatsapp")]
[Authorize(Policy = "manage_whatsapp")]
public class WhatsAppController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IUserDepartmentFeatureAccessService _userDepartmentFeatureAccessService;

    public WhatsAppController(
        IWhatsAppService whatsAppService,
        IUserDepartmentFeatureAccessService userDepartmentFeatureAccessService)
    {
        _whatsAppService = whatsAppService;
        _userDepartmentFeatureAccessService = userDepartmentFeatureAccessService;
    }


    private async Task<IActionResult?> EnsureWhatsAppEnabledAsync()
    {
        var enabled =
            await _userDepartmentFeatureAccessService
                .CanCurrentUserAccessFeatureAsync("whatsAppEnabled");

        if (!enabled)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = "WhatsApp feature is disabled." });
        }

        return null;
    }


    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var featureError = await EnsureWhatsAppEnabledAsync();
        if (featureError != null)
            return featureError;

        var status = await _whatsAppService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    [HttpGet("test-connection")]
    public async Task<IActionResult> TestConnection(CancellationToken cancellationToken)
    {
        var featureError = await EnsureWhatsAppEnabledAsync();
        if (featureError != null)
            return featureError;
        var diagnostics = await _whatsAppService.TestConnectionAsync(cancellationToken);
        return Ok(diagnostics);
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect(CancellationToken cancellationToken)
    {
        var featureError = await EnsureWhatsAppEnabledAsync();
        if (featureError != null)
            return featureError;
        var payload = await _whatsAppService.InitializeConnectAsync(cancellationToken);
        return Ok(payload);
    }

    [HttpPost("complete")]
    public async Task<IActionResult> CompleteEmbeddedSignup([FromBody] WhatsAppEmbeddedSignupCompleteRequest request, CancellationToken cancellationToken)
    {
        var featureError = await EnsureWhatsAppEnabledAsync();
        if (featureError != null)
            return featureError;
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
        var featureError = await EnsureWhatsAppEnabledAsync();
        if (featureError != null)
            return featureError;
        await _whatsAppService.DisconnectAsync(cancellationToken);
        return Ok(new { message = "WhatsApp disconnected." });
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates(CancellationToken cancellationToken)
    {
        var featureError = await EnsureWhatsAppEnabledAsync();
        if (featureError != null)
            return featureError;
        var templates = await _whatsAppService.GetTemplatesAsync(cancellationToken);
        return Ok(templates);
    }

    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages(CancellationToken cancellationToken)
    {
        var featureError = await EnsureWhatsAppEnabledAsync();
        if (featureError != null)
            return featureError;

        var messages = await _whatsAppService.GetMessagesAsync(cancellationToken);
        return Ok(messages);
    }
}
