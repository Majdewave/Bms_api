using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

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
    [Authorize]
    public async Task<IActionResult> CreateCheckoutSession()
    {
        try
        {
            // Get tenantId from JWT claims
            var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id" || c.Type.EndsWith("/tenantid", StringComparison.OrdinalIgnoreCase));
            if (tenantIdClaim == null || !Guid.TryParse(tenantIdClaim.Value, out var tenantId))
            {
                return Unauthorized(new { error = "Tenant ID not found in token." });
            }

            // Call StripeService
            var url = await _stripeService.CreateCheckoutSessionAsync(tenantId, Clienta.Api.Models.PlanType.Pro, Clienta.Api.Models.BillingCycle.Monthly);
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
        try
        {
            StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];

                if (string.IsNullOrEmpty(session_id))
                return BadRequest("Missing session_id");

            var sessionService = new SessionService();
            var session = await sessionService.GetAsync(session_id);

            if (session == null)
                return BadRequest("Session not found");

            if (session.PaymentStatus != "paid")
                return BadRequest("Payment not completed");

            var customerId = session.CustomerId;
            Console.WriteLine("CONFIRM customerId: " + customerId);

            if (string.IsNullOrEmpty(customerId))
                return BadRequest("Missing customerId");

          
            Tenant? tenant = null; 

            for (int i = 0; i < 10; i++)
            {
                 tenant = await _context.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.StripeCustomerId == customerId);

                if (tenant != null)
                    break;

                await Task.Delay(1500); // מחכה ל-webhook
            }

            if (tenant == null)
                return BadRequest("Tenant not found (webhook not completed yet)");

            var user = await _context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Role == "Admin");

            //  אם לא נמצא - נתקן
            if (user == null)
            {
                user = await _context.Users
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u =>
                        u.Role == "Admin" &&
                        u.TenantId == tenant.Id
                    );

                if (user == null)
                    return BadRequest("Admin user not found");

                user.TenantId = tenant.Id;
                await _context.SaveChangesAsync();
            }

            // גם אם נמצא אבל tenantId ריק
            if (user.TenantId == Guid.Empty)
            {
                user.TenantId = tenant.Id;
                await _context.SaveChangesAsync();
            }

            if (user == null)
                return BadRequest("Admin user not found");

            var token = _tokenService.GenerateJwtToken(user);

            return Ok(new { token });
        }
        catch (Exception ex)
        {
            Console.WriteLine("🔥 ERROR confirm-session: " + ex);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // =========================
    // WEBHOOK
    // =========================
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        try
        {
            
            var stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                _config["Stripe:WebhookSecret"],
                throwOnApiVersionMismatch: false
            );


            if (stripeEvent.Type == "checkout.session.completed")
            {
                var session = stripeEvent.Data.Object as Session;
                if (session == null)
                    return Ok();

                var customerId = session.CustomerId;
                var subscriptionId = session.SubscriptionId;
                Console.WriteLine("CONFIRM customerId: " + customerId);

                if (string.IsNullOrEmpty(customerId))
                    return Ok();

                if (session.Metadata == null ||
                    !session.Metadata.TryGetValue("tenant_id", out var tenantIdString) ||
                    !Guid.TryParse(tenantIdString, out var tenantId))
                {
                    return Ok();
                }

                var tenant = await _context.Tenants.FindAsync(tenantId);
                if (tenant == null)
                    return Ok();

                // Update tenant on payment success
                tenant.StripeCustomerId = customerId;
                tenant.StripeSubscriptionId = subscriptionId;
                tenant.Plan = PlanType.Pro;
                tenant.SubscriptionStatus = SubscriptionStatus.Active;
                tenant.IsSuspended = false;

                await _context.SaveChangesAsync();
                Console.WriteLine("✅ Tenant updated from webhook (plan upgraded to Pro, subscription active, not suspended)");
            }

            return Ok(); //  תמיד OK
        }
        catch (Exception ex)
        {
            Console.WriteLine("🔥 WEBHOOK ERROR: " + ex);
            return Ok(); //  לא מחזירים 400 ל-Stripe
        }
    }
}