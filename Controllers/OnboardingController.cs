using Microsoft.AspNetCore.Mvc;
using Clienta.Api.Services;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/onboarding")]
public class OnboardingController : ControllerBase
{
    private readonly IOnboardingService _service;

    public OnboardingController(IOnboardingService service)
    {
        _service = service;
    }

    [HttpPost("signup")]
    public async Task<IActionResult> Signup(SignupRequest request)
    {
        try
        {
            var message = await _service.CreateTenantAsync(
                request.CompanyName,
                request.Subdomain,
                request.Email,
                request.Password);

            return Ok(new { message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromQuery] string token)
    {
        try
        {
            var domain = await _service.VerifyEmailAsync(token);
            return Ok(new { redirect = $"https://{domain}/login" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record SignupRequest(
    string CompanyName,
    string Subdomain,
    string Email,
    string Password);
