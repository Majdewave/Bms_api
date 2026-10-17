using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public class TenantProductServiceTests
{
    [TestMethod]
    public void Resolve_MapsNullToLegacyAndNeverToClinic()
    {
        var service = new TenantProductService(null!);

        Assert.AreEqual(ResolvedTenantProduct.Legacy, service.Resolve(null));
        Assert.AreEqual(ResolvedTenantProduct.Clinic, service.Resolve(TenantProductType.Clinic));
        Assert.AreEqual(ResolvedTenantProduct.Business, service.Resolve(TenantProductType.Business));
    }

    [TestMethod]
    public void ToApiValue_UsesStableStrings()
    {
        var service = new TenantProductService(null!);

        Assert.AreEqual("legacy", service.ToApiValue(ResolvedTenantProduct.Legacy));
        Assert.AreEqual("clinic", service.ToApiValue(ResolvedTenantProduct.Clinic));
        Assert.AreEqual("business", service.ToApiValue(ResolvedTenantProduct.Business));
    }

    [TestMethod]
    public async Task GetAsync_EmptyTenantIdResolvesToLegacyWithoutTouchingTheDatabase()
    {
        var service = new TenantProductService(null!);

        Assert.AreEqual(ResolvedTenantProduct.Legacy, await service.GetAsync(Guid.Empty));
    }
}
