using Clienta.Api.Entities;
using Clienta.Api.Models;

namespace Clienta.Api.Services;

public static class StripePlanResolver
{
    public static string GetPriceId(
        IConfiguration configuration,
        TenantProductType? productType,
        PlanType plan,
        BillingCycle cycle)
    {
        string configKey;

        if (productType == TenantProductType.Clinic)
        {
            configKey = (plan, cycle) switch
            {
                (PlanType.Basic, BillingCycle.Monthly) => "Stripe:Clinic:BasicMonthlyPriceId",
                (PlanType.Pro, BillingCycle.Monthly) => "Stripe:Clinic:ProMonthlyPriceId",
                (PlanType.Enterprise, BillingCycle.Monthly) => "Stripe:Clinic:EnterpriseMonthlyPriceId",
                (_, BillingCycle.Yearly) => throw new InvalidOperationException("Clinic subscriptions currently support monthly billing only."),
                _ => throw new InvalidOperationException("Invalid Clinic plan configuration")
            };
        }
        else
        {
            // Preserve the existing shared Stripe mapping for Business, Legacy, and other non-Clinic flows.
            configKey = (plan, cycle) switch
            {
                (PlanType.Basic, BillingCycle.Monthly) => "Stripe:BasicMonthlyPriceId",
                (PlanType.Basic, BillingCycle.Yearly) => throw new InvalidOperationException("Yearly billing not available for Basic plan. Upgrade to Pro for yearly billing."),
                (PlanType.Pro, BillingCycle.Monthly) => "Stripe:ProMonthlyPriceId",
                (PlanType.Pro, BillingCycle.Yearly) => "Stripe:ProYearlyPriceId",
                _ => throw new InvalidOperationException("Invalid plan configuration")
            };
        }

        var priceId = configuration[configKey];
        if (string.IsNullOrWhiteSpace(priceId))
        {
            var productName = productType == TenantProductType.Clinic ? "Clinic " : string.Empty;
            throw new InvalidOperationException($"Price ID not configured for {productName}{plan} {cycle}");
        }

        return priceId;
    }

    public static Dictionary<string, string> CreateCheckoutMetadata(Tenant tenant, PlanType plan, BillingCycle cycle)
    {
        var metadata = new Dictionary<string, string>
        {
            ["tenant_id"] = tenant.Id.ToString(),
            ["subdomain"] = tenant.Subdomain,
            ["plan_type"] = plan.ToString(),
            ["billing_cycle"] = cycle.ToString()
        };

        if (tenant.ProductType == TenantProductType.Clinic)
        {
            metadata["product_type"] = TenantProductType.Clinic.ToString();
        }

        return metadata;
    }

    public static bool TryResolveClinicCheckout(
        IReadOnlyDictionary<string, string> metadata,
        TenantProductType? tenantProductType,
        out PlanType plan,
        out BillingCycle cycle)
    {
        plan = default;
        cycle = default;

        if (tenantProductType != TenantProductType.Clinic ||
            !metadata.TryGetValue("product_type", out var productType) ||
            !string.Equals(productType, TenantProductType.Clinic.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        if (!metadata.TryGetValue("plan_type", out var planValue) ||
            !Enum.TryParse(planValue, ignoreCase: false, out plan) ||
            plan is not (PlanType.Basic or PlanType.Pro or PlanType.Enterprise))
        {
            return false;
        }

        if (!metadata.TryGetValue("billing_cycle", out var cycleValue) ||
            !Enum.TryParse(cycleValue, ignoreCase: false, out cycle) ||
            cycle != BillingCycle.Monthly)
        {
            return false;
        }

        return true;
    }
}