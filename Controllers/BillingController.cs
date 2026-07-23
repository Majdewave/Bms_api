using Stripe.BillingPortal;
using Stripe;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Clienta.Api.Services;
using Clienta.Api.Data;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController : ControllerBase
{

    private readonly IStripeService _stripeService;
    private readonly ITenantContext _tenantContext;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        IStripeService stripeService,
        ITenantContext tenantContext,
        AppDbContext db,
        IConfiguration configuration,
        ILogger<BillingController> logger)
    {
        _stripeService = stripeService;
        _tenantContext = tenantContext;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }


    [Authorize]
    [HttpPost("portal")]
    public async Task<IActionResult> CreatePortalSession()
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            if (tenantId == Guid.Empty)
            {
                _logger.LogWarning("Billing portal request rejected: missing tenant context.");
                return BadRequest(new { error = "Tenant context missing" });
            }

            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == tenantId);

            if (tenant == null)
            {
                _logger.LogWarning("Billing portal request rejected: tenant {TenantId} not found.", tenantId);
                return BadRequest(new { error = "Tenant not found" });
            }

            if (string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
            {
                _logger.LogWarning(
                    "Billing portal request rejected: Stripe customer missing for tenant {TenantId}. SubscriptionId={SubscriptionId}, Plan={Plan}, Status={Status}",
                    tenant.Id,
                    tenant.StripeSubscriptionId,
                    tenant.Plan,
                    tenant.SubscriptionStatus);

                return BadRequest(new { error = "Stripe customer missing" });
            }

            var baseUrl = (_configuration["App:BaseUrl"] ?? "https://clienta.digitalpenpro.com").TrimEnd('/');
            var returnUrl = $"{baseUrl}/billing";

            var options = new SessionCreateOptions
            {
                Customer = tenant.StripeCustomerId,
                ReturnUrl = returnUrl
            };

            _logger.LogInformation(
                "Creating Stripe billing portal session. TenantId={TenantId}, CustomerId={CustomerId}, SubscriptionId={SubscriptionId}, ReturnUrl={ReturnUrl}",
                tenant.Id,
                tenant.StripeCustomerId,
                tenant.StripeSubscriptionId,
                returnUrl);

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return Ok(new { url = session.Url });
        }
        catch (StripeException ex)
        {
            _logger.LogError(
                ex,
                "Stripe billing portal error. Code={Code}, Type={Type}, Param={Param}, HttpStatus={HttpStatus}, Message={Message}",
                ex.StripeError?.Code,
                ex.StripeError?.Type,
                ex.StripeError?.Param,
                ex.HttpStatusCode,
                ex.StripeError?.Message ?? ex.Message);

            return BadRequest(new
            {
                error = ex.StripeError?.Message ?? ex.Message,
                stripe = new
                {
                    code = ex.StripeError?.Code,
                    type = ex.StripeError?.Type,
                    param = ex.StripeError?.Param,
                    httpStatus = (int?)ex.HttpStatusCode
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected billing portal error.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unexpected billing portal failure" });
        }
    }


    [HttpGet("status")]
    public async Task<IActionResult> GetBillingStatus()
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return NotFound(new { error = "Tenant not found" });

            var daysRemaining = tenant.Plan == PlanType.Trial && tenant.TrialEndsAt.HasValue
                ? (tenant.TrialEndsAt.Value - DateTime.UtcNow).Days 
                : -1;

            return Ok(new
            {
                plan = tenant.Plan.ToString(),
                billingCycle = tenant.BillingCycle.ToString(),
                subscriptionStatus = tenant.SubscriptionStatus.ToString(),
                subscriptionEndsAt = tenant.SubscriptionEndsAt,
                id = tenant.Id,
                trialEndsAt = tenant.TrialEndsAt,
                daysRemaining = daysRemaining > 0 ? daysRemaining : 0,
                userLimit = tenant.UserLimit,
                messageLimit = tenant.MessageLimit,
                isSuspended = tenant.IsSuspended,
                stripeCustomerId = tenant.StripeCustomerId,
                features = new
                {
                    maxUsers = tenant.UserLimit,
                    maxMessages = tenant.MessageLimit,
                    customBranding = tenant.Plan != PlanType.Trial,
                    emailAutomation = true,
                    priority = tenant.Plan == PlanType.Pro ? "High" : "Standard",
                    support = tenant.Plan == PlanType.Pro ? "24/7 Priority" : "Email only"
                }
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("upgrade")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] UpgradeRequest request)
    {
        try
        {
            if (!Enum.IsDefined(typeof(PlanType), request.PlanType) || request.PlanType == PlanType.Trial)
            {
                return BadRequest(new { error = "Invalid plan type. Choose Basic or Pro." });
            }

            if (!Enum.IsDefined(typeof(BillingCycle), request.BillingCycle))
            {
                return BadRequest(new { error = "Invalid billing cycle. Choose Monthly or Yearly." });
            }

            // Basic plan only supports Monthly billing
            if (request.PlanType == PlanType.Basic && request.BillingCycle == BillingCycle.Yearly)
            {
                return BadRequest(new { error = "Yearly billing not available for Basic plan. Upgrade to Pro for yearly billing." });
            }

            var tenantId = _tenantContext.TenantId;
            var checkoutUrl = await _stripeService.CreateCheckoutSessionAsync(tenantId, request.PlanType, request.BillingCycle);

            return Ok(new { url = checkoutUrl });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("change-plan")]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request)
    {
        try
        {
            if (!Enum.IsDefined(typeof(PlanType), request.NewPlan) || request.NewPlan == PlanType.Trial)
            {
                return BadRequest(new { error = "Invalid plan type. Choose Basic or Pro." });
            }

            if (!Enum.IsDefined(typeof(BillingCycle), request.NewCycle))
            {
                return BadRequest(new { error = "Invalid billing cycle. Choose Monthly or Yearly." });
            }

            // Basic plan only supports Monthly billing
            if (request.NewPlan == PlanType.Basic && request.NewCycle == BillingCycle.Yearly)
            {
                return BadRequest(new { error = "Yearly billing not available for Basic plan. Upgrade to Pro for yearly billing." });
            }

            var tenantId = _tenantContext.TenantId;
            
            // Get current tenant to determine if upgrade or downgrade
            var tenant = await _db.Tenants.FindAsync(tenantId);
            if (tenant == null)
                return NotFound(new { error = "Tenant not found" });

            // Determine if this is an upgrade or downgrade
            bool isUpgrade = IsUpgrade(tenant.Plan, tenant.BillingCycle, request.NewPlan, request.NewCycle);

            await _stripeService.ChangePlanAsync(tenantId, request.NewPlan, request.NewCycle);

            if (isUpgrade)
            {
                return Ok(new 
                { 
                    success = true,
                    type = "upgrade",
                    message = "Plan upgraded successfully! Proration will be charged on next invoice.",
                    currentPlan = tenant?.Plan == null ? string.Empty : tenant.Plan.ToString(),
                    currentCycle = tenant.BillingCycle.ToString(),
                    newPlan = request.NewPlan.ToString(),
                    newCycle = request.NewCycle.ToString(),
                    effectiveImmediately = true
                });
            }
            else
            {
                // Get updated tenant with scheduled info
                tenant = await _db.Tenants.FindAsync(tenantId);
                
                return Ok(new 
                { 
                    success = true,
                    type = "downgrade",
                    message = "Downgrade scheduled successfully. You'll continue to enjoy your current plan until the end of your billing period.",
                    currentPlan = tenant.Plan.ToString(),
                    currentCycle = tenant.BillingCycle.ToString(),
                    scheduledPlan = tenant.ScheduledPlan?.ToString(),
                    scheduledDate = tenant.ScheduledPlanChangeAt,
                    effectiveImmediately = false,
                    note = "No refund will be issued. Your plan will change automatically at the end of your billing period."
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private bool IsUpgrade(PlanType currentPlan, BillingCycle currentCycle, PlanType newPlan, BillingCycle newCycle)
    {
        if (currentPlan != newPlan)
            return newPlan > currentPlan;
        return newCycle > currentCycle;
    }
}

public record UpgradeRequest(PlanType PlanType, BillingCycle BillingCycle);
public record ChangePlanRequest(PlanType NewPlan, BillingCycle NewCycle);
