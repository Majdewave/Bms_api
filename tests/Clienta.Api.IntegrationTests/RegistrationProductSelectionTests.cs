using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class RegistrationProductSelectionTests
{
    [DataTestMethod]
    [DataRow("clinic", (int)TenantProductType.Clinic)]
    [DataRow("business", (int)TenantProductType.Business)]
    [DataRow(" Clinic ", (int)TenantProductType.Clinic)]
    [DataRow("BUSINESS", (int)TenantProductType.Business)]
    public void RegistrationProductParser_AcceptsOnlyExplicitClinicOrBusiness(string value, int expectedValue)
    {
        var parsed = TenantProductService.TryParseRegistrationProduct(value, out var productType);

        Assert.IsTrue(parsed);
        Assert.AreEqual((TenantProductType)expectedValue, productType);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("legacy")]
    [DataRow("unknown")]
    [DataRow("0")]
    [DataRow("1")]
    [DataRow("2")]
    public void RegistrationProductParser_RejectsMissingLegacyUnknownAndNumericValues(string? value)
    {
        Assert.IsFalse(TenantProductService.TryParseRegistrationProduct(value, out _));
    }

    [DataTestMethod]
    [DataRow((int)TenantProductType.Clinic, 9)]
    [DataRow((int)TenantProductType.Business, 17)]
    [DataRow(null, 23)]
    public void TrialDurationResolver_UsesProductSettingsAndKeepsNullLegacy(int? productValue, int expectedDays)
    {
        TenantProductType? productType = productValue.HasValue
            ? (TenantProductType)productValue.Value
            : null;
        var settings = new PlatformSettingsDto(
            Guid.NewGuid(), "support@example.test", null, null,
            23, 9, 17, 3, true, true, true, true, false,
            46m, 460m, "Legacy Pro", true, 1, DateTime.UtcNow);

        Assert.AreEqual(expectedDays, TrialDurationResolver.ResolveDays(productType, settings));
    }

    [TestMethod]
    public void NewRegistrationTenant_DefaultsToTrialPendingAndDoesNotStartTrialDates()
    {
        var tenant = new Tenant();

        Assert.AreEqual(PlanType.Trial, tenant.Plan);
        Assert.AreEqual(SubscriptionStatus.PendingApproval, tenant.SubscriptionStatus);
        Assert.IsNull(tenant.TrialStartsAt);
        Assert.IsNull(tenant.TrialEndsAt);
        Assert.IsFalse(tenant.IsTrial);
        Assert.IsNull(tenant.ProductType); // Set only after a valid Clinic/Business registration choice is parsed.
    }

    [TestMethod]
    public void ExistingNullProductType_RemainsLegacyAndValid()
    {
        var tenant = new Tenant { Plan = PlanType.Pro, ProductType = null };
        var productResolver = new TenantProductService(null!);

        Assert.AreEqual(ResolvedTenantProduct.Legacy, productResolver.Resolve(tenant.ProductType));
        Assert.AreEqual(PlanType.Pro, tenant.Plan);
    }
}