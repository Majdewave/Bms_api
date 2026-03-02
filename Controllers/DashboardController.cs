using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Clienta.Api.Data;
using Clienta.Api.Services;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPlanProvider _planProvider;

    public DashboardController(
        AppDbContext db,
        ITenantContext tenantContext,
        IPlanProvider planProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _planProvider = planProvider;
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboard()
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            
            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return NotFound(new { error = "Tenant not found" });

            var currentUsers = await _db.Users
                .CountAsync(u => u.TenantId == tenantId);

            var currentAppointments = await _db.Appointments
                .CountAsync(a => a.TenantId == tenantId);

            var currentNotes = await _db.Notes
                .CountAsync(n => n.TenantId == tenantId);

            var totalMessages = currentAppointments + currentNotes;

            var daysRemaining = tenant.Plan == PlanType.Trial && tenant.TrialEndsAt.HasValue
                ? (int)(tenant.TrialEndsAt.Value - DateTime.UtcNow).TotalDays
                : -1;

            var hoursRemaining = tenant.Plan == PlanType.Trial && tenant.TrialEndsAt.HasValue
                ? (int)(tenant.TrialEndsAt.Value - DateTime.UtcNow).TotalHours
                : -1;

            // Smart upgrade recommendation
            object? upgradeRecommendation = null;
            if (tenant.Plan == PlanType.Trial && daysRemaining <= 7 && daysRemaining >= 0)
            {
                var basicPlan = _planProvider.GetPlan(PlanType.Basic);
                upgradeRecommendation = new
                {
                    show = true,
                    message = "Upgrade now and unlock:",
                    targetPlan = "Basic",
                    benefits = new[]
                    {
                        $"✔ {basicPlan.UserLimit} users",
                        $"✔ {basicPlan.MessageLimit:N0} messages",
                        "✔ Custom branding"
                    },
                    ctaText = "Upgrade Now",
                    urgency = daysRemaining <= 2 ? "high" : "medium"
                };
            }

            // Pricing options with savings highlighted
            // Basic: Monthly only, Pro: Monthly + Yearly
            var pricingOptions = new
            {
                basic = new
                {
                    monthly = new
                    {
                        price = 39,
                        currency = "ILS",
                        display = "₪39/month",
                        planType = 1,
                        billingCycle = 0,
                        available = true
                    },
                    yearly = new
                    {
                        available = false,
                        message = "Upgrade to Pro for yearly billing"
                    }
                },
                pro = new
                {
                    monthly = new
                    {
                        price = 69,
                        currency = "ILS",
                        display = "₪69/month",
                        planType = 2,
                        billingCycle = 0,
                        available = true
                    },
                    yearly = new
                    {
                        price = 690,
                        currency = "ILS",
                        display = "₪690/year",
                        savings = "Save ₪138",
                        savingsAmount = 138,
                        monthlyEquivalent = "₪57.50/month",
                        planType = 2,
                        billingCycle = 1,
                        recommended = true,
                        available = true
                    }
                }
            };

            return Ok(new
            {
                tenant = new
                {
                    name = tenant.Name,
                    subdomain = tenant.Subdomain,
                    plan = tenant.Plan,
                    billingCycle = tenant.BillingCycle.ToString(),
                    subscriptionStatus = tenant.SubscriptionStatus.ToString(),
                    trialEndsAt = tenant.TrialEndsAt,
                    isSuspended = tenant.IsSuspended,
                    scheduledPlanChange = tenant.ScheduledPlan.HasValue ? new
                    {
                        currentPlan = tenant.Plan.ToString(),
                        scheduledPlan = tenant.ScheduledPlan?.ToString(),
                        effectiveDate = tenant.ScheduledPlanChangeAt,
                        message = $"Your plan will change to {tenant.ScheduledPlan} on {tenant.ScheduledPlanChangeAt:MMM dd, yyyy}"
                    } : null
                },
                trial = new
                {
                    isActive = tenant.Plan == PlanType.Trial && tenant.TrialEndsAt.HasValue && tenant.TrialEndsAt > DateTime.UtcNow,
                    endsAt = tenant.TrialEndsAt,
                    daysRemaining = daysRemaining > 0 ? daysRemaining : 0,
                    hoursRemaining = hoursRemaining > 0 ? hoursRemaining : 0,
                    isExpired = tenant.Plan == PlanType.Trial && tenant.TrialEndsAt.HasValue && tenant.TrialEndsAt < DateTime.UtcNow
                },
                usage = new
                {
                    users = new
                    {
                        current = currentUsers,
                        limit = tenant.UserLimit,
                        percentage = tenant.UserLimit > 0 ? (currentUsers * 100) / tenant.UserLimit : 0
                    },
                    messages = new
                    {
                        current = totalMessages,
                        limit = tenant.MessageLimit,
                        percentage = tenant.MessageLimit > 0 ? (totalMessages * 100) / tenant.MessageLimit : 0
                    }
                },
                features = new
                {
                    customBranding = tenant.Plan != PlanType.Trial,
                    emailAutomation = true,
                    priority = tenant.Plan == PlanType.Pro ? "High" : "Standard",
                    support = tenant.Plan == PlanType.Pro ? "24/7 Priority" : "Email only"
                },
                pricing = pricingOptions,
                upgrade = upgradeRecommendation
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
