# 📉 Scheduled Downgrade Flow

## Overview
When a customer on **Pro Yearly** wants to downgrade to **Basic**, the system handles it gracefully:
- ✅ **No immediate change** - Customer keeps Pro access until period end
- ✅ **No refund** - Customer already paid for the full period
- ✅ **Automatic switch** - Stripe applies the change at billing cycle end
- ✅ **Clear messaging** - Customer knows exactly when the change happens

---

## 🔄 How It Works

### 1. Customer Initiates Downgrade

```http
POST /api/billing/change-plan
Authorization: Bearer {jwt_token}

{
  "newPlan": 1,      // 1 = Basic
  "newCycle": 0      // 0 = Monthly
}
```

### 2. System Response

**For Upgrades (Basic→Pro or Monthly→Yearly):**
```json
{
  "success": true,
  "type": "upgrade",
  "message": "Plan upgraded successfully! Proration will be charged on next invoice.",
  "effectiveImmediately": true
}
```

**For Downgrades (Pro→Basic or Yearly→Monthly):**
```json
{
  "success": true,
  "type": "downgrade",
  "message": "Downgrade scheduled successfully. You'll continue to enjoy your current plan until the end of your billing period.",
  "currentPlan": "Pro",
  "currentCycle": "Yearly",
  "scheduledPlan": "Basic",
  "scheduledDate": "2026-03-15T00:00:00Z",
  "effectiveImmediately": false,
  "note": "No refund will be issued. Your plan will change automatically at the end of your billing period."
}
```

---

## 🎯 Implementation Details

### Database Fields (Tenant Entity)

```csharp
public PlanType? ScheduledPlan { get; set; }
public DateTime? ScheduledPlanChangeAt { get; set; }
```

**Simple Design:**
- `ScheduledPlan`: The target plan (e.g., Basic)
- `ScheduledPlanChangeAt`: When the change will take effect
- BillingCycle is updated immediately to match the new plan

### Upgrade vs Downgrade Logic

```csharp
private bool IsUpgrade(PlanType currentPlan, BillingCycle currentCycle, 
                       PlanType newPlan, BillingCycle newCycle)
{
    // Plan tier change
    if (currentPlan != newPlan)
        return newPlan > currentPlan; // Basic(1) → Pro(2) = Upgrade

    // Billing cycle change  
    return newCycle > currentCycle; // Monthly(0) → Yearly(1) = Upgrade
}
```

### Stripe API Usage

**Upgrade** (immediate with proration):
```csharp
var updateOptions = new SubscriptionUpdateOptions
{
    Items = new List<SubscriptionItemOptions>
    {
        new() { Id = subscription.Items.Data[0].Id, Price = newPriceId }
    },
    ProrationBehavior = "create_prorations" // Charge difference now
};
```

**Downgrade** (scheduled at period end):
```csharp
var updateOptions = new SubscriptionUpdateOptions
{
    Items = new List<SubscriptionItemOptions>
    {
        new() { Id = subscription.Items.Data[0].Id, Price = newPriceId }
    },
    ProrationBehavior = "none" // No refund, no proration
};
// Stripe automatically applies at period end when using "none"
```

---

## 📊 Dashboard Display

```json
{
  "tenant": {
    "plan": "Pro",
    "billingCycle": "Yearly",
    "scheduledPlanChange": {
      "currentPlan": "Pro",
      "scheduledPlan": "Basic",
      "effectiveDate": "2026-03-15T00:00:00Z",
      "message": "Your plan will change to Basic on Mar 15, 2026"
    }
  }
}
```

---

## 🔔 Webhook Handling

When Stripe webhook fires `customer.subscription.updated` at period end:

```csharp
private async Task HandleSubscriptionUpdated(Event stripeEvent)
{
    var subscription = stripeEvent.Data.Object as Subscription;
    var tenant = await _db.Tenants
        .FirstOrDefaultAsync(t => t.StripeSubscriptionId == subscription.Id);

    // Check if scheduled plan change should be applied
    if (tenant.ScheduledPlan.HasValue && 
        tenant.ScheduledPlanChangeAt.HasValue && 
        DateTime.UtcNow >= tenant.ScheduledPlanChangeAt.Value)
    {
        // Apply the scheduled plan change
        var planDef = planProvider.GetPlan(tenant.ScheduledPlan.Value);
        
        tenant.Plan = tenant.ScheduledPlan.Value;
        tenant.UserLimit = planDef.UserLimit;
        tenant.MessageLimit = planDef.MessageLimit;

        // Clear scheduled fields
        tenant.ScheduledPlan = null;
        tenant.ScheduledPlanChangeAt = null;
    }

    await _db.SaveChangesAsync();
}
```

---

## ✨ UX Best Practices

### Show Clear Warning
```
⚠️ Downgrade to Basic Monthly

You're currently on Pro Yearly (paid until Mar 15, 2026).

If you downgrade:
✅ You'll keep Pro access until Mar 15, 2026
✅ No refund for unused time
✅ Automatically switches to Basic Monthly on Mar 15
✅ You can cancel this scheduled change anytime

[Confirm Downgrade]  [Cancel]
```

### Display Scheduled Change Banner
```
ℹ️ Scheduled Plan Change
Your plan will change to Basic Monthly on Mar 15, 2026.
[Cancel Scheduled Change]
```

---

## 🔁 Canceling a Scheduled Downgrade

To cancel a scheduled downgrade, just upgrade again:

```http
POST /api/billing/change-plan

{
  "newPlan": 2,      // Stay on Pro
  "newCycle": 1      // Stay on Yearly
}
```

This will:
- Clear `ScheduledPlan` and `ScheduledPlanChangeAt`
- Keep current plan active
- No charges (already paid)

---

## 📋 Testing Checklist

- [ ] Pro Yearly → Basic Monthly: Schedules correctly
- [ ] Basic Monthly → Pro Yearly: Applies immediately with proration
- [ ] Pro Yearly → Pro Monthly: Schedules correctly
- [ ] Pro Monthly → Pro Yearly: Applies immediately with proration
- [ ] Dashboard shows scheduled downgrade info
- [ ] Webhook applies scheduled change at period end
- [ ] Can cancel scheduled downgrade by upgrading again
- [ ] Clear error messages for invalid combinations

---

## 🎯 Production Notes

1. **Stripe Configuration**: `ProrationBehavior = "none"` ensures no refunds for downgrades
2. **Webhook Timing**: Stripe fires `customer.subscription.updated` at period end
3. **Date Estimation**: We use 30-day estimate until webhook provides exact date
4. **Customer Communication**: Always show when change takes effect
5. **Fairness**: Customer already paid, so they keep access until period ends

This approach is:
- ✅ Fair to customers (no money lost)
- ✅ Good for business (no refunds to process)
- ✅ Simple to implement (Stripe handles timing)
- ✅ Clear UX (customer knows exactly what happens)
