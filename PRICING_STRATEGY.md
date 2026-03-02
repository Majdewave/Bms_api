# 💰 Pricing Strategy - Simplified Structure

## 🎯 Two-Tier Approach

### Basic Plan
- **Monthly only**: ₪39/month
- **Target**: Entry-level customers who want to try the service
- **No yearly option**: Keeps it simple and encourages Pro upgrades

### Pro Plan
- **Monthly**: ₪69/month
- **Yearly**: ₪690/year (save ₪138 = 2 months free!)
- **Target**: Committed customers who want better value
- **Yearly discount**: Perfect incentive for annual commitment

### Pricing Structure

```
BASIC
├── Monthly: ₪39/month
└── Yearly: Not available (upgrade to Pro)

PRO
├── Monthly: ₪69 × 12 = ₪828/year
└── Yearly: ₪690/year (save ₪138 = 2 months)
```

**Key Insights:**
- Basic yearly removed to reduce complexity
- Pro yearly offers 16.7% discount (2 months free)
- Clear upgrade path from Basic to Pro
- Simpler pricing = easier decision for customers

---

## 🧠 Clean Code Implementation

### 1. Enum Definition
```csharp
public enum BillingCycle
{
    Monthly = 0,
    Yearly = 1
}
```

### 2. Stripe Price Mapping
```csharp
public static class StripePlans
{
    // Basic Plan
    public const string BasicMonthly = "price_basic_monthly";
    public const string BasicYearly = "price_basic_yearly";

    // Pro Plan
    public const string ProMonthly = "price_pro_monthly";
    public const string ProYearly = "price_pro_yearly";
}
```

### 3. Clean Switch Expression (No if-elses!)
```csharp
private string GetPriceId(PlanType plan, BillingCycle cycle)
{
    var configKey = (plan, cycle) switch
    {
        (PlanType.Basic, BillingCycle.Monthly) => "Stripe:BasicMonthlyPriceId",
        (PlanType.Basic, BillingCycle.Yearly) => "Stripe:BasicYearlyPriceId",
        (PlanType.Pro, BillingCycle.Monthly) => "Stripe:ProMonthlyPriceId",
        (PlanType.Pro, BillingCycle.Yearly) => "Stripe:ProYearlyPriceId",
        _ => throw new InvalidOperationException("Invalid plan configuration")
    };

    var priceId = _configuration[configKey];
    if (string.IsNullOrEmpty(priceId))
        throw new InvalidOperationException($"Price ID not configured for {plan} {cycle}");

    return priceId;
}
```

---

## 📊 Conversion Psychology

### Why Simplified Tiers Work

1. **Basic Monthly Only**
   - Reduces decision paralysis
   - Entry-level customers prefer monthly flexibility
   - No commitment required for testing
   - Simple pricing = faster conversions

2. **Pro with Yearly Option**
   - Committed customers want savings
   - Yearly discount as upgrade incentive
   - 16.7% savings feels significant
   - Rewards annual commitment

3. **Clear Upgrade Path**
   - Basic → Pro Monthly (same billing cycle)
   - Basic → Pro Yearly (save ₪138!)
   - No confusion about Basic yearly
   - "Want yearly? Upgrade to Pro!"

### Frontend Presentation Tips

```
BASIC
₪39/month
Billed monthly only
[Upgrade to Pro for yearly billing]

vs.

PRO — ✨ Best Value!

YEARLY
₪690/year
Save ₪138 (2 months free!)
₪57.50/month equivalent

vs.

MONTHLY
₪69/month
Billed monthly
```

**Key elements:**
- Disable yearly toggle for Basic users
- Show "Upgrade to Pro" message for Basic yearly
- Emphasize Pro yearly savings prominently
- Display monthly equivalent (₪57.50/month)
- Use "2 months free" language (more tangible than "16% off")
- Mark yearly as "Most Popular" to nudge behavior
- Display monthly price equivalent: "₪32.50/month when billed yearly"

---

## 🎨 UI/UX Best Practices

### Pricing Page Toggle
```jsx
<PricingToggle>
  <Option active={cycle === 'monthly'}>Monthly</Option>
  <Option active={cycle === 'yearly'}>
    Yearly 
    <Badge>Save 2 months</Badge>
  </Option>
</PricingToggle>
```

### Price Display
```jsx
{cycle === 'yearly' ? (
  <div>
    <Price>₪390</Price>
    <Period>/year</Period>
    <Savings>₪32.50/month · Save ₪78</Savings>
  </div>
) : (
  <div>
    <Price>₪39</Price>
    <Period>/month</Period>
  </div>
)}
```

---

## 📈 A/B Testing Ideas

### Test 1: Messaging
- **A**: "Save 2 months"
- **B**: "Save ₪78"
- **Hypothesis**: Months are more tangible than money

### Test 2: Default Selection
- **A**: Default to Monthly
- **B**: Default to Yearly
- **Hypothesis**: Pre-selecting yearly increases conversions

### Test 3: Urgency
- **A**: No urgency
- **B**: "Limited time: 2 months free"
- **Hypothesis**: False urgency might backfire

---

## 🔧 Configuration Matrix

| Plan | Cycle | Price | Annual Cost | Savings | Stripe Price ID |
|------|-------|-------|-------------|---------|-----------------|
| Basic | Monthly | ₪39 | ₪468 | - | `price_basic_monthly` |
| Basic | Yearly | ₪390 | ₪390 | ₪78 | `price_basic_yearly` |
| Pro | Monthly | ₪69 | ₪828 | - | `price_pro_monthly` |
| Pro | Yearly | ₪690 | ₪690 | ₪138 | `price_pro_yearly` |

---

## 💡 Pro Tips

1. **Always show both options**
   - Don't hide monthly to push yearly
   - Transparency builds trust

2. **Highlight savings clearly**
   - Use green badges/text
   - Show "₪32.50/month" for yearly
   - Emphasize "2 months free"

3. **No surprise charges**
   - Clear "Billed annually" text
   - Show renewal date
   - Send renewal reminder 7 days before

4. **Easy upgrades**
   - Allow switching mid-term
   - Prorate the difference
   - No penalty for changing

5. **Grace period applies to both**
   - 3 days grace for failed payments
   - Works for monthly and yearly
   - Builds customer loyalty

---

## 🚀 Implementation Checklist

- [x] BillingCycle enum created
- [x] StripePlans with 4 price IDs
- [x] GetPriceId() switch expression
- [x] Tenant.BillingCycle field
- [x] API accepts billingCycle parameter
- [x] Webhook saves billing cycle
- [x] Dashboard displays cycle
- [x] Migration applied
- [x] Documentation updated

---

## 🎯 Success Metrics

Track these KPIs:
- **Yearly vs Monthly** conversion rate
- **Annual Recurring Revenue (ARR)** growth
- **Churn rate** by billing cycle
- **Lifetime Value (LTV)** yearly vs monthly
- **Average contract value**

**Expected Results:**
- 30-40% choose yearly
- Yearly customers have 2x lower churn
- Yearly customers have 3x higher LTV
