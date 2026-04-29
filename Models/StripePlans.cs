namespace Clienta.Api.Models;

/// <summary>
/// Stripe Price IDs for subscription plans
/// </summary>
/// <remarks>
/// Pricing Structure:
/// - Basic: ₪39/month only (no yearly option)
/// - Pro: ₪69/month or ₪690/year (save 2 months!)
/// 
/// To get these IDs:
/// 1. Go to Stripe Dashboard → Products
/// 2. Create Product "Clienta Basic":
///    - Price ₪39/month (monthly recurring)
/// 3. Create Product "Clienta Pro":
///    - Price ₪69/month (monthly recurring)
///    - Price ₪690/year (yearly recurring)
/// 4. Copy the price_xxxxx IDs to appsettings.json
/// </remarks>
public static class StripePlans
{
    // Basic Plan - Monthly only
    public const string BasicMonthly = "price_basic_monthly";

    // Pro Plan - Monthly + Yearly
    public const string ProMonthly = "price_pro_monthly";
    public const string ProYearly = "price_pro_yearly";
}
