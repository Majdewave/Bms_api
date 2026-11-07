using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class StripePlanResolverTests
{
    private static IConfiguration CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:Clinic:BasicMonthlyPriceId"] = "test-clinic-basic-config-value",
            ["Stripe:Clinic:ProMonthlyPriceId"] = "test-clinic-pro-config-value",
            ["Stripe:Clinic:EnterpriseMonthlyPriceId"] = "test-clinic-enterprise-config-value",
            ["Stripe:BasicMonthlyPriceId"] = "test-generic-basic-config-value",
            ["Stripe:ProMonthlyPriceId"] = "test-generic-pro-config-value",
            ["Stripe:ProYearlyPriceId"] = "test-generic-pro-yearly-config-value"
        })
        .Build();

    [TestMethod]
    public void ClinicBasicMonthly_UsesOnlyClinicBasicConfiguration()
    {
        var configuration = CreateConfiguration();

        var result = StripePlanResolver.GetPriceId(configuration, TenantProductType.Clinic, PlanType.Basic, BillingCycle.Monthly);

        Assert.AreEqual("test-clinic-basic-config-value", result);
    }

    [TestMethod]
    public void ClinicProMonthly_UsesOnlyClinicProConfiguration()
    {
        var configuration = CreateConfiguration();

        var result = StripePlanResolver.GetPriceId(configuration, TenantProductType.Clinic, PlanType.Pro, BillingCycle.Monthly);

        Assert.AreEqual("test-clinic-pro-config-value", result);
    }

    [TestMethod]
    public void ClinicEnterpriseMonthly_UsesClinicEnterpriseConfigurationWithoutRenamingEnum()
    {
        var configuration = CreateConfiguration();

        var result = StripePlanResolver.GetPriceId(configuration, TenantProductType.Clinic, PlanType.Enterprise, BillingCycle.Monthly);

        Assert.AreEqual("test-clinic-enterprise-config-value", result);
        Assert.AreEqual("Enterprise", PlanType.Enterprise.ToString());
    }

    [TestMethod]
    public void BusinessProMonthly_ContinuesUsingExistingGenericConfiguration()
    {
        var configuration = CreateConfiguration();

        var result = StripePlanResolver.GetPriceId(configuration, TenantProductType.Business, PlanType.Pro, BillingCycle.Monthly);

        Assert.AreEqual("test-generic-pro-config-value", result);
    }

    [TestMethod]
    public void LegacyProMonthly_ContinuesUsingExistingGenericConfiguration()
    {
        var configuration = CreateConfiguration();

        var result = StripePlanResolver.GetPriceId(configuration, null, PlanType.Pro, BillingCycle.Monthly);

        Assert.AreEqual("test-generic-pro-config-value", result);
    }

    [TestMethod]
    public void ClinicCheckoutMetadata_IdentifiesProductAndCommercialPlanValue()
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Subdomain = "clinic-test",
            ProductType = TenantProductType.Clinic
        };

        var metadata = StripePlanResolver.CreateCheckoutMetadata(tenant, PlanType.Enterprise, BillingCycle.Monthly);

        Assert.AreEqual("Clinic", metadata["product_type"]);
        Assert.AreEqual("Enterprise", metadata["plan_type"]);
        Assert.AreEqual("Monthly", metadata["billing_cycle"]);
    }

    [TestMethod]
    public void ClinicCheckoutWebhookMapping_ResolvesEnterpriseForClinicTenant()
    {
        var metadata = new Dictionary<string, string>
        {
            ["product_type"] = "Clinic",
            ["plan_type"] = "Enterprise",
            ["billing_cycle"] = "Monthly"
        };

        var accepted = StripePlanResolver.TryResolveClinicCheckout(
            metadata,
            TenantProductType.Clinic,
            out var plan,
            out var cycle);

        Assert.IsTrue(accepted);
        Assert.AreEqual(PlanType.Enterprise, plan);
        Assert.AreEqual(BillingCycle.Monthly, cycle);
    }

    [TestMethod]
    public void ClinicCheckoutWebhookMapping_RejectsOtherProductAndMissingClinicMarker()
    {
        var clinicMetadata = new Dictionary<string, string>
        {
            ["product_type"] = "Clinic",
            ["plan_type"] = "Pro",
            ["billing_cycle"] = "Monthly"
        };
        var unmarkedMetadata = new Dictionary<string, string>
        {
            ["plan_type"] = "Pro",
            ["billing_cycle"] = "Monthly"
        };

        Assert.IsFalse(StripePlanResolver.TryResolveClinicCheckout(
            clinicMetadata,
            TenantProductType.Business,
            out _,
            out _));
        Assert.IsFalse(StripePlanResolver.TryResolveClinicCheckout(
            clinicMetadata,
            null,
            out _,
            out _));
        Assert.IsFalse(StripePlanResolver.TryResolveClinicCheckout(
            unmarkedMetadata,
            TenantProductType.Clinic,
            out _,
            out _));
    }

    [TestMethod]
    public void ClinicCheckoutMetadata_DoesNotMarkLegacyCheckoutAsClinic()
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Subdomain = "legacy-test",
            ProductType = null
        };

        var metadata = StripePlanResolver.CreateCheckoutMetadata(tenant, PlanType.Pro, BillingCycle.Monthly);

        Assert.IsFalse(metadata.ContainsKey("product_type"));
    }

    [TestMethod]
    public void ClinicPriceResolution_DoesNotFallBackToGenericPriceWhenClinicPriceIsMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:ProMonthlyPriceId"] = "test-generic-pro-config-value"
            })
            .Build();

        Assert.ThrowsException<InvalidOperationException>(() =>
            StripePlanResolver.GetPriceId(configuration, TenantProductType.Clinic, PlanType.Pro, BillingCycle.Monthly));
    }
}
