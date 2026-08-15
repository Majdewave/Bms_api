using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/stripe")]
[AllowAnonymous]
public class StripeController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly Clienta.Api.Services.TokenService _tokenService;
    private readonly Clienta.Api.Services.IStripeService _stripeService;

    public StripeController(AppDbContext context, IConfiguration config, Clienta.Api.Services.TokenService tokenService, Clienta.Api.Services.IStripeService stripeService)
    {
        _context = context;
        _config = config;
        _tokenService = tokenService;
        _stripeService = stripeService;

    }
    /// <summary>
    /// Create a Stripe checkout session for Pro/Monthly
    /// </summary>
    [HttpPost("create-checkout-session")]
    [AllowAnonymous]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CheckoutRequest request)
    {
        try
        {
            Console.WriteLine("=== CHECKOUT REQUEST ===");
            Console.WriteLine($"TenantId: {request.TenantId}");
            Console.WriteLine($"Plan: {request.Plan}");
            Console.WriteLine($"BillingCycle: {request.BillingCycle}");

            var tenantId = request.TenantId;

            if (tenantId == Guid.Empty)
            {
                return BadRequest(new { error = "TenantId is required" });
            }
            // Call StripeService
            var url = await _stripeService.CreateCheckoutSessionAsync(tenantId, Clienta.Api.Models.PlanType.Pro, request.BillingCycle);
            return Ok(new { url });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            // Log error as needed
            return StatusCode(500, new { error = "An error occurred while creating the checkout session.", details = ex.Message });
        }
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok("STRIPE CONTROLLER WORKING");
    }

    // =========================
    // SUCCESS FLOW 
    // =========================
    [HttpGet("confirm-session")]
    public async Task<IActionResult> ConfirmSession([FromQuery] string session_id)
    {
        const int maxRetries = 10;
        const int retryDelayMs = 2000;

        try
        {
            StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];

            if (string.IsNullOrWhiteSpace(session_id))
            {
                return BadRequest(new
                {
                    error = "missing_session_id",
                    message = "Missing session_id."
                });
            }

            var sessionService = new SessionService();
            Session? session;

            try
            {
                session = await sessionService.GetAsync(session_id);
            }
            catch (StripeException ex)
            {
                Console.WriteLine($"❌ Stripe session fetch failed for {session_id}: {ex.Message}");
                return BadRequest(new
                {
                    error = "session_not_found",
                    message = "Session not found."
                });
            }

            if (session == null)
            {
                return BadRequest(new
                {
                    error = "session_not_found",
                    message = "Session not found."
                });
            }

            Console.WriteLine($"ConfirmSession SessionId: {session.Id}");
            Console.WriteLine($"ConfirmSession PaymentStatus: {session.PaymentStatus}");

            if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    error = "payment_not_completed",
                    message = "Payment not completed."
                });
            }

            if (session.Metadata == null ||
                !session.Metadata.TryGetValue("tenant_id", out var tenantIdRaw) ||
                string.IsNullOrWhiteSpace(tenantIdRaw))
            {
                return BadRequest(new
                {
                    error = "tenant_metadata_missing",
                    message = "tenant_id metadata missing from Stripe session."
                });
            }

            if (!Guid.TryParse(tenantIdRaw, out var tenantId))
            {
                return BadRequest(new
                {
                    error = "tenant_metadata_missing",
                    message = "tenant_id metadata is invalid."
                });
            }

            Console.WriteLine($"ConfirmSession TenantId(metadata): {tenantId}");

            Tenant? tenant = null;

            for (int i = 0; i < maxRetries; i++)
            {
                tenant = await _context.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == tenantId);

                if (tenant == null)
                {
                    break;
                }

                if (tenant.Plan != PlanType.Trial && tenant.SubscriptionStatus == SubscriptionStatus.Active)
                {
                    break;
                }

                if (i < maxRetries - 1)
                {
                    await Task.Delay(retryDelayMs);
                }
            }

            if (tenant == null)
            {
                return BadRequest(new
                {
                    error = "tenant_not_found",
                    message = "Tenant not found."
                });
            }

            if (tenant.Plan == PlanType.Trial || tenant.SubscriptionStatus != SubscriptionStatus.Active)
            {
                return StatusCode(409, new
                {
                    error = "subscription_processing",
                    message = "Payment completed. Subscription is still being activated."
                });
            }

            var user = await _context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Role == "Admin");

            if (user == null)
            {
                return BadRequest(new
                {
                    error = "admin_user_not_found",
                    message = "Admin user not found."
                });
            }

            var token = _tokenService.GenerateJwtToken(user);

            return Ok(new
            {
                success = true,
                token,
                plan = tenant.Plan.ToString(),
                subscriptionStatus = tenant.SubscriptionStatus.ToString()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("🔥 ERROR confirm-session: " + ex);
            return StatusCode(500, new
            {
                error = "server_error",
                message = "An unexpected error occurred while confirming payment."
            });
        }
    }

    // =========================
    // WEBHOOK
    // =========================
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body)
            .ReadToEndAsync();

        var signature = Request.Headers["Stripe-Signature"].ToString();

        try
        {
            await _stripeService.HandleWebhookAsync(json, signature);

            return Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine("🔥 WEBHOOK ERROR: " + ex);
            return StatusCode(500);
        }
    }

    public class CheckoutRequest
    {
        public string Plan { get; set; }
        public Guid TenantId { get; set; }
        public BillingCycle BillingCycle { get; set; }
    }
}