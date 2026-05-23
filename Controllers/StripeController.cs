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
    private readonly IEmailService _emailService;

    public StripeController(AppDbContext context, IConfiguration config, Clienta.Api.Services.TokenService tokenService, Clienta.Api.Services.IStripeService stripeService, IEmailService emailService)
    {
        _context = context;
        _config = config;
        _tokenService = tokenService;
        _stripeService = stripeService;
        _emailService = emailService;

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
            var tenantId = request.TenantId;

            if (tenantId == Guid.Empty)
            {
                return BadRequest(new { error = "TenantId is required" });
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


            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                {
                    var session = stripeEvent.Data.Object as Session;
                    if (session == null)
                        return Ok();

                    var customerId = session.CustomerId;
                    var subscriptionId = session.SubscriptionId;
                    Console.WriteLine("CONFIRM customerId: " + customerId);

                    // send mail to TENANT after Register
                    session = stripeEvent.Data.Object as Session;

                    var email = session?.CustomerDetails?.Email;
                    var html = "Payment Suucceed";

                    if (!string.IsNullOrEmpty(email))
                    {
                        await _emailService.SendEmailAsync(
                            email,
                             "🎉 התשלום בוצע בהצלחה",

                             html = @"<div style='font-family:Arial, sans-serif; direction:rtl; background:#f9fafb; padding:40px'>
                                    <div style='max-width:500px; margin:auto; background:white; border-radius:10px; padding:30px; text-align:center; box-shadow:0 4px 12px rgba(0,0,0,0.05)'>

                                        <p style='font-size:16px; color:#374151; margin-bottom:20px;'>
                                            החשבון שלך שודרג בהצלחה לתוכנית פרימיום.
                                        </p>

                                        <div style='margin:25px 0;'>
                                            <a href='https://clienta.digitalpenpro.com/login'
                                               style='background:#2563eb; color:white; padding:12px 24px; border-radius:6px; text-decoration:none; font-size:14px;'>
                                                מעבר למערכת
                                            </a>
                                        </div>

                                        <hr style='margin:30px 0; border:none; border-top:1px solid #e5e7eb;' />

                                        <p style='font-size:13px; color:#6b7280; margin:0;'>
                                            תודה שבחרת ב־Clienta 💙
                                        </p>

                                        <p style='font-size:13px; color:#6b7280; margin-top:5px;'>
                                            Clienta Team
                                        </p>

                                    </div>
                                </div>"
                            );
                        }


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
                    break;
                }
                case "customer.subscription.updated":
                {
                    var subscription = stripeEvent.Data.Object as Subscription;

                    if (subscription == null)
                        break;

                    var tenant = await _context.Tenants
                        .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscription.Id);

                    if (tenant != null)
                    {
                        tenant.SubscriptionStatus =
                            subscription.Status switch
                            {
                                "active" => SubscriptionStatus.Active,
                                "trialing" => SubscriptionStatus.Active,
                                "past_due" => SubscriptionStatus.PastDue,
                                "canceled" => SubscriptionStatus.Canceled,
                                "unpaid" => SubscriptionStatus.Unpaid,
                                _ => SubscriptionStatus.Canceled
                            };

                        var currentPeriodEnd =
                            subscription.Items.Data
                                .FirstOrDefault()
                                ?.CurrentPeriodEnd;

                        tenant.SubscriptionEndsAt =
                            currentPeriodEnd;

                        await _context.SaveChangesAsync();
                    }

                    break;
                }
                case "customer.subscription.deleted":
                {
                    var subscription = stripeEvent.Data.Object as Subscription;

                    var tenant = await _context.Tenants
                        .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscription.Id);

                    if (tenant != null)
                    {
                        tenant.SubscriptionStatus =
                            SubscriptionStatus.Canceled;

                        tenant.SubscriptionEndsAt =
                            DateTime.UtcNow;

                        await _context.SaveChangesAsync();
                    }

                    break;
                }
            }

            return Ok(); //  תמיד OK
        }
        catch (Exception ex)
        {
            Console.WriteLine("🔥 WEBHOOK ERROR: " + ex);
            return Ok(); //  לא מחזירים 400 ל-Stripe
        }
    }

    public class CheckoutRequest
    {
        public string Plan { get; set; }
        public Guid TenantId { get; set; }
    }
}