using Stripe;
using Stripe.Checkout;
using Clienta.Api.Data;
using Clienta.Api.Models;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IStripeService
{
    Task<string> CreateCheckoutSessionAsync(Guid tenantId, PlanType planType, BillingCycle billingCycle);
    Task ChangePlanAsync(Guid tenantId, PlanType newPlan, BillingCycle newCycle);
    Task HandleWebhookAsync(string json, string stripeSignature);
}

public class StripeService : IStripeService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;

    public StripeService(AppDbContext db, IConfiguration configuration, IEmailService emailService)
    {
        _db = db;
        _configuration = configuration;
        _emailService = emailService;
        
        // Set Stripe API key
        StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
    }

    /// <summary>
    /// Get the correct Stripe Price ID based on plan and billing cycle
    /// Note: Basic plan only supports Monthly billing
    /// </summary>
    private string GetPriceId(PlanType plan, BillingCycle cycle)
    {
        var configKey = (plan, cycle) switch
        {
            (PlanType.Basic, BillingCycle.Monthly) => "Stripe:BasicMonthlyPriceId",
            (PlanType.Basic, BillingCycle.Yearly) => throw new InvalidOperationException("Yearly billing not available for Basic plan. Upgrade to Pro for yearly billing."),
            (PlanType.Pro, BillingCycle.Monthly) => "Stripe:ProMonthlyPriceId",
            (PlanType.Pro, BillingCycle.Yearly) => "Stripe:ProYearlyPriceId",
            _ => throw new InvalidOperationException("Invalid plan configuration")
        };

        var priceId = _configuration[configKey];
        if (string.IsNullOrEmpty(priceId))
            throw new InvalidOperationException($"Price ID not configured for {plan} {cycle}");

        return priceId;
    }

    /// <summary>
    /// Change existing subscription plan
    /// - UPGRADE (Basic→Pro or Monthly→Yearly): Immediate with proration
    /// - DOWNGRADE (Pro→Basic or Yearly→Monthly): Scheduled at period end
    /// </summary>
    public async Task ChangePlanAsync(Guid tenantId, PlanType newPlan, BillingCycle newCycle)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        
        if (tenant == null)
            throw new InvalidOperationException("Tenant not found");

        if (tenant.Plan == PlanType.Trial)
            throw new InvalidOperationException("Cannot change plan for trial tenants. Use upgrade endpoint instead.");

        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId))
            throw new InvalidOperationException("No active subscription found");

        // Check if already on this plan
        if (tenant.Plan == newPlan && tenant.BillingCycle == newCycle)
            throw new InvalidOperationException("Already subscribed to this plan");

        // Get new price ID
        var newPriceId = GetPriceId(newPlan, newCycle);

        // Get existing subscription from Stripe
        var subscriptionService = new SubscriptionService();
        var subscription = await subscriptionService.GetAsync(tenant.StripeSubscriptionId);

        if (subscription == null || subscription.Items.Data.Count == 0)
            throw new InvalidOperationException("Subscription not found in Stripe");

        // Determine if this is an upgrade or downgrade
        bool isUpgrade = IsUpgrade(tenant.Plan, tenant.BillingCycle, newPlan, newCycle);

        SubscriptionUpdateOptions updateOptions;

        if (isUpgrade)
        {
            // UPGRADE: Immediate change with proration
            updateOptions = new SubscriptionUpdateOptions
            {
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = subscription.Items.Data[0].Id,
                        Price = newPriceId
                    }
                },
                ProrationBehavior = "create_prorations" // Charge prorated amount now
            };

            // Update immediately
            await subscriptionService.UpdateAsync(subscription.Id, updateOptions);

            // Update tenant in database
            var planProvider = new PlanProvider();
            var planDef = planProvider.GetPlan(newPlan);

            tenant.Plan = newPlan;
            tenant.BillingCycle = newCycle;
            tenant.UserLimit = planDef.UserLimit;
            tenant.MessageLimit = planDef.MessageLimit;
            
            // Clear any scheduled downgrade
            tenant.ScheduledPlan = null;
            tenant.ScheduledPlanChangeAt = null;
        }
        else
        {
            // DOWNGRADE: Schedule for end of billing period
            updateOptions = new SubscriptionUpdateOptions
            {
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = subscription.Items.Data[0].Id,
                        Price = newPriceId
                    }
                },
                ProrationBehavior = "none" // No proration, no refund
            };

            // Schedule the change (Stripe will apply it at period end)
            await subscriptionService.UpdateAsync(subscription.Id, updateOptions);

            // Store scheduled downgrade in database
            tenant.ScheduledPlan = newPlan;
            tenant.BillingCycle = newCycle; // Update cycle now for scheduled change
            // Stripe will notify via webhook when change is applied
            tenant.ScheduledPlanChangeAt = DateTime.UtcNow.AddDays(30); // Estimate (webhook updates actual)
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Determine if a plan change is an upgrade or downgrade
    /// Upgrade: Basic→Pro, Monthly→Yearly
    /// Downgrade: Pro→Basic, Yearly→Monthly
    /// </summary>
    private bool IsUpgrade(PlanType currentPlan, BillingCycle currentCycle, PlanType newPlan, BillingCycle newCycle)
    {
        // Plan tier change
        if (currentPlan != newPlan)
        {
            // Basic(1) → Pro(2) = Upgrade
            // Pro(2) → Basic(1) = Downgrade
            return newPlan > currentPlan;
        }

        // Same plan, billing cycle change
        // Monthly(0) → Yearly(1) = Upgrade
        // Yearly(1) → Monthly(0) = Downgrade
        return newCycle > currentCycle;
    }

    public async Task<string> CreateCheckoutSessionAsync(Guid tenantId, PlanType planType, BillingCycle billingCycle)
    {
        var tenant = await _db.Tenants
            .Include(t => t.Users)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
            throw new InvalidOperationException("Tenant not found");

        if (planType == PlanType.Trial)
            throw new InvalidOperationException("Cannot create checkout for Trial plan");

        var adminEmail = await _db.Users
            .Where(u => u.TenantId == tenantId && u.Role == "Admin")
            .Select(u => u.Email)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(adminEmail))
            throw new InvalidOperationException("Admin email not found");

        // Get the correct price ID based on plan and billing cycle
        var priceId = GetPriceId(planType, billingCycle);

        var options = new SessionCreateOptions
        {
            CustomerEmail = adminEmail,
            Mode = "subscription",
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Price = priceId,
                    Quantity = 1
                }
            },
           // SuccessUrl = $"https://{tenant.Subdomain}.yourapp.com/billing/success?session_id={{CHECKOUT_SESSION_ID}}",
           // CancelUrl = $"https://{tenant.Subdomain}.yourapp.com/billing",
            SuccessUrl = "https://clienta.digitalpenpro.com/success?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = "https://clienta.digitalpenpro.com/billing",
            ClientReferenceId = tenantId.ToString(),
            Metadata = new Dictionary<string, string>
            {
                { "tenant_id", tenantId.ToString() },
                { "subdomain", tenant.Subdomain },
                { "plan_type", planType.ToString() },
                { "billing_cycle", billingCycle.ToString() }
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);

        return session.Url;
    }

    public async Task HandleWebhookAsync(string json, string stripeSignature)
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                json,
                stripeSignature,
                webhookSecret,
                throwOnApiVersionMismatch: false
                );

            Console.WriteLine($"Stripe event received: {stripeEvent.Type}");
            Console.WriteLine($"Stripe EventId: {stripeEvent.Id}");

            //  Idempotency: Check if event already processed
            if (await _db.ProcessedStripeEvents.AnyAsync(e => e.EventId == stripeEvent.Id))
            {
                Console.WriteLine($"Stripe event already processed, skipping: {stripeEvent.Id}");
                return; // Already processed, skip
            }

            // Process the event
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutSessionCompleted(stripeEvent);
                    break;

                case "customer.subscription.updated":
                    await HandleSubscriptionUpdated(stripeEvent);
                    break;

                case "customer.subscription.deleted":
                    await HandleSubscriptionDeleted(stripeEvent);
                    break;

                case "invoice.payment_succeeded":
                    await HandleInvoicePaymentSucceeded(stripeEvent);
                    break;

                case "invoice.payment_failed":
                    await HandleInvoicePaymentFailed(stripeEvent);
                    break;

                default:
                    Console.WriteLine($"Unhandled Stripe event type: {stripeEvent.Type}");
                    break;
            }

            // Mark event as processed
            _db.ProcessedStripeEvents.Add(new ProcessedStripeEvent
            {
                EventId = stripeEvent.Id,
                ProcessedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
        catch (StripeException e)
        {
            throw new InvalidOperationException($"Stripe webhook error: {e.Message}");
        }
    }

    private async Task HandleCheckoutSessionCompleted(Event stripeEvent)
    {
        var sessionId = stripeEvent.Data.Object switch
        {
            Stripe.Checkout.Session s => s.Id,
            _ => null
        };

        if (sessionId == null)
        {
            Console.WriteLine("❌ Stripe checkout session ID is null");
            return;
        }

        Console.WriteLine($"Checkout Session ID: {sessionId}");

        // fetch fresh session from Stripe
        var sessionService = new SessionService();
        var session = await sessionService.GetAsync(sessionId);

        if (session == null)
        {
            Console.WriteLine("❌ Stripe checkout session not found when re-fetching");
            return;
        }

        Console.WriteLine($"PaymentStatus: {session.PaymentStatus}");

        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"❌ Stripe checkout payment is not paid. SessionId={session.Id}, PaymentStatus={session.PaymentStatus}");
            return;
        }

        if (session.Metadata == null)
        {
            Console.WriteLine("❌ Stripe checkout metadata is null");
            return;
        }

        if (!session.Metadata.TryGetValue("tenant_id", out var tenantIdString))
        {
            Console.WriteLine("❌ Stripe checkout metadata tenant_id missing");
            return;
        }

        if (!Guid.TryParse(tenantIdString, out var tenantId))
        {
            Console.WriteLine("❌ Stripe checkout metadata tenant_id invalid");
            return;
        }

        if (!session.Metadata.TryGetValue("plan_type", out var planTypeStr) || string.IsNullOrWhiteSpace(planTypeStr))
        {
            Console.WriteLine("❌ Stripe checkout metadata plan_type missing");
            return;
        }

        if (!Enum.TryParse<PlanType>(planTypeStr, true, out var planType))
        {
            Console.WriteLine($"❌ Stripe checkout metadata plan_type invalid: {planTypeStr}");
            return;
        }

        if (!session.Metadata.TryGetValue("billing_cycle", out var billingCycleStr) || string.IsNullOrWhiteSpace(billingCycleStr))
        {
            Console.WriteLine("❌ Stripe checkout metadata billing_cycle missing");
            return;
        }

        if (!Enum.TryParse<BillingCycle>(billingCycleStr, true, out var billingCycle))
        {
            Console.WriteLine($"❌ Stripe checkout metadata billing_cycle invalid: {billingCycleStr}");
            return;
        }

        var tenant = await _db.Tenants.FindAsync(tenantId);

        if (tenant == null)
        {
            Console.WriteLine($"❌ Tenant not found for tenant_id: {tenantId}");
            return;
        }

        Console.WriteLine("✅ Stripe checkout completed");
        Console.WriteLine($"TenantId: {tenantId}");
        Console.WriteLine($"CustomerId: {session.CustomerId}");
        Console.WriteLine($"SubscriptionId: {session.SubscriptionId}");
        Console.WriteLine($"Plan: {planType}");
        Console.WriteLine($"BillingCycle: {billingCycle}");

        // Get plan configuration from PlanProvider
        var planProvider = new PlanProvider();
        var planDef = planProvider.GetPlan(planType);

        tenant.StripeCustomerId = session.CustomerId;
        tenant.StripeSubscriptionId = session.SubscriptionId;
        tenant.Plan = planType;
        tenant.BillingCycle = billingCycle;
        tenant.SubscriptionStatus = SubscriptionStatus.Active;
        tenant.IsSuspended = false;
        tenant.UserLimit = planDef.UserLimit;
        tenant.MessageLimit = planDef.MessageLimit;
        tenant.PaymentGracePeriodEndsAt = null; // Clear grace period

        await _db.SaveChangesAsync();

        try
        {
            var email = session.CustomerDetails?.Email;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var html = @"<div style='font-family:Arial, sans-serif; direction:rtl; background:#f9fafb; padding:40px'>
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
                            תודה שבחרת ב-Clienta
                        </p>
                        <p style='font-size:13px; color:#6b7280; margin-top:5px;'>
                            Clienta Team
                        </p>
                    </div>
                </div>";

                await _emailService.SendEmailAsync(
                    email,
                    "🎉 החשבון שלך שודרג ל-Pro",
                    html
                );
            }
        }
        catch (Exception emailEx)
        {
            Console.WriteLine("⚠️ Customer payment email failed: " + emailEx);
        }

        try
        {
            await _emailService.SendEmailAsync(
                "mjd.salman@gmail.com",
                "New Paid Subscription in Clienta",
                $@"<h2>New Paid Subscription in Clienta</h2>
                <p><strong>Tenant Name:</strong> {tenant.Name}</p>
                <p><strong>Tenant ID:</strong> {tenant.Id}</p>
                <p><strong>Email:</strong> {session.CustomerDetails?.Email}</p>
                <p><strong>Stripe Customer ID:</strong> {session.CustomerId}</p>
                <p><strong>Stripe Subscription ID:</strong> {session.SubscriptionId}</p>
                <p><strong>Plan:</strong> {planType}</p>
                <p><strong>Billing Cycle:</strong> {billingCycle}</p>"
            );
        }
        catch (Exception adminEmailEx)
        {
            Console.WriteLine("⚠️ Admin payment email failed: " + adminEmailEx);
        }

        Console.WriteLine("✅ Tenant upgraded successfully to Pro");
    }

    private async Task HandleSubscriptionUpdated(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.StripeSubscriptionId == subscription.Id);

        if (tenant == null) return;

        // Check if a scheduled plan change should be applied
        if (tenant.ScheduledPlan.HasValue && 
            tenant.ScheduledPlanChangeAt.HasValue && 
            DateTime.UtcNow >= tenant.ScheduledPlanChangeAt.Value)
        {
            // Apply the scheduled plan change
            var planProvider = new PlanProvider();
            var planDef = planProvider.GetPlan(tenant.ScheduledPlan.Value);

            tenant.Plan = tenant.ScheduledPlan.Value;
            tenant.UserLimit = planDef.UserLimit;
            tenant.MessageLimit = planDef.MessageLimit;

            // Clear scheduled plan change
            tenant.ScheduledPlan = null;
            tenant.ScheduledPlanChangeAt = null;
        }

        // Map Stripe subscription status to our enum
        tenant.SubscriptionStatus = subscription.Status switch
        {
            "trialing" => SubscriptionStatus.Trialing,
            "active" => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" => SubscriptionStatus.Canceled,
            "unpaid" => SubscriptionStatus.Unpaid,
            _ => SubscriptionStatus.Active
        };

        // Suspend if not active/trialing
        tenant.IsSuspended = !(subscription.Status == "active" || subscription.Status == "trialing");
        
        await _db.SaveChangesAsync();
    }

    private async Task HandleSubscriptionDeleted(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.StripeSubscriptionId == subscription.Id);

        if (tenant == null) return;

        tenant.Plan = PlanType.Trial; // Downgrade to trial
        tenant.SubscriptionStatus = SubscriptionStatus.Canceled;
        tenant.IsSuspended = true; // Suspend account
        tenant.UserLimit = 10;
        tenant.MessageLimit = 100;
        
        await _db.SaveChangesAsync();
    }

    private async Task HandleInvoicePaymentSucceeded(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        if (invoice == null) return;

        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);

        if (tenant == null) return;

        // Payment succeeded - clear grace period and unsuspend
        tenant.SubscriptionStatus = SubscriptionStatus.Active;
        tenant.IsSuspended = false;
        tenant.PaymentGracePeriodEndsAt = null;
        
        await _db.SaveChangesAsync();
    }

    private async Task HandleInvoicePaymentFailed(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        if (invoice == null) return;

        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);

        if (tenant == null) return;

        // 🧠 Grace Period: Don't suspend immediately!
        // Give 3 days grace period for payment retry
        if (!tenant.PaymentGracePeriodEndsAt.HasValue)
        {
            tenant.SubscriptionStatus = SubscriptionStatus.GracePeriod;
            tenant.PaymentGracePeriodEndsAt = DateTime.UtcNow.AddDays(3);
            await _db.SaveChangesAsync();

            // TODO: Send email notification about payment failure
            // "Your payment failed. Please update your payment method within 3 days."
        }
        
        await Task.CompletedTask;
    }
}
