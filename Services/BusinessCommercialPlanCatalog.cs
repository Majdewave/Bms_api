using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public enum BusinessCapability
{
    Dashboard,
    Clients,
    ClientContacts,
    Projects,
    Quotes,
    Invoices,
    ProjectBillingMilestones,
    Payments,
    ProjectFiles,
    FinancialDashboard
}

public sealed record BusinessCommercialPlanDefinition(
    CommercialPlanConfiguration Configuration,
    IReadOnlyList<BusinessCapability> Capabilities);

public interface IBusinessCommercialPlanCatalog
{
    Task<IReadOnlyList<BusinessCommercialPlanDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BusinessCommercialPlanDefinition?> GetAsync(PlanType planType, CancellationToken cancellationToken = default);
    Task<BusinessCommercialPlanDefinition?> UpdateAsync(
        PlanType planType,
        UpdateBusinessPlanConfigurationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class BusinessCommercialPlanCatalog : IBusinessCommercialPlanCatalog
{
    private static readonly IReadOnlyList<BusinessCapability> ReleasedCapabilities = Array.AsReadOnly(
    [
        BusinessCapability.Dashboard,
        BusinessCapability.Clients,
        BusinessCapability.ClientContacts,
        BusinessCapability.Projects,
        BusinessCapability.Quotes,
        BusinessCapability.Invoices,
        BusinessCapability.ProjectBillingMilestones,
        BusinessCapability.Payments,
        BusinessCapability.ProjectFiles,
        BusinessCapability.FinancialDashboard
    ]);

    private readonly MasterDbContext _db;

    public BusinessCommercialPlanCatalog(MasterDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BusinessCommercialPlanDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var configurations = await _db.CommercialPlanConfigurations
            .AsNoTracking()
            .Where(configuration => configuration.ProductType == TenantProductType.Business)
            .OrderBy(configuration => configuration.DisplayOrder)
            .ThenBy(configuration => configuration.PlanType)
            .ToListAsync(cancellationToken);

        return configurations.Select(CreateDefinition).ToArray();
    }

    public async Task<BusinessCommercialPlanDefinition?> GetAsync(
        PlanType planType,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _db.CommercialPlanConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ProductType == TenantProductType.Business && item.PlanType == planType,
                cancellationToken);

        return configuration is null ? null : CreateDefinition(configuration);
    }

    public async Task<BusinessCommercialPlanDefinition?> UpdateAsync(
        PlanType planType,
        UpdateBusinessPlanConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var configuration = await _db.CommercialPlanConfigurations
            .SingleOrDefaultAsync(
                item => item.ProductType == TenantProductType.Business && item.PlanType == planType,
                cancellationToken);

        if (configuration is null)
        {
            return null;
        }

        configuration.DisplayName = request.DisplayName.Trim();
        configuration.Description = NormalizeOptional(request.Description);
        configuration.MonthlyPrice = request.MonthlyPrice;
        configuration.AnnualPrice = request.AnnualPrice;
        configuration.Enabled = request.Enabled;
        configuration.DisplayOrder = request.DisplayOrder;
        configuration.AvailableForPurchase = IsAvailableForPurchase(planType);
        configuration.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return CreateDefinition(configuration);
    }

    public static bool IsAvailableForPurchase(PlanType planType) => planType == PlanType.Basic;

    private static BusinessCommercialPlanDefinition CreateDefinition(CommercialPlanConfiguration configuration)
    {
        configuration.AvailableForPurchase = IsAvailableForPurchase(configuration.PlanType);
        return new BusinessCommercialPlanDefinition(configuration, ReleasedCapabilities);
    }

    private static void Validate(UpdateBusinessPlanConfigurationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 100)
        {
            throw new ArgumentException("Display name must contain between 1 and 100 characters.", nameof(request));
        }

        if (request.MonthlyPrice < 0 || request.AnnualPrice < 0)
        {
            throw new ArgumentException("Display prices cannot be negative.", nameof(request));
        }

        if (request.DisplayOrder < 0)
        {
            throw new ArgumentException("Display order cannot be negative.", nameof(request));
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CommercialPlanResolution(
    ResolvedTenantProduct Product,
    PlanType Plan,
    PlanDefinition? ExistingPlan,
    BusinessCommercialPlanDefinition? BusinessPlan);

public interface ICommercialPlanResolver
{
    Task<CommercialPlanResolution> ResolveAsync(
        TenantProductType? productType,
        PlanType planType,
        CancellationToken cancellationToken = default);
}

public sealed class CommercialPlanResolver : ICommercialPlanResolver
{
    private readonly IPlanProvider _legacyPlanProvider;
    private readonly IBusinessCommercialPlanCatalog _businessCatalog;

    public CommercialPlanResolver(IPlanProvider legacyPlanProvider, IBusinessCommercialPlanCatalog businessCatalog)
    {
        _legacyPlanProvider = legacyPlanProvider;
        _businessCatalog = businessCatalog;
    }

    public async Task<CommercialPlanResolution> ResolveAsync(
        TenantProductType? productType,
        PlanType planType,
        CancellationToken cancellationToken = default)
    {
        if (productType != TenantProductType.Business)
        {
            var product = productType == TenantProductType.Clinic
                ? ResolvedTenantProduct.Clinic
                : ResolvedTenantProduct.Legacy;

            return new CommercialPlanResolution(product, planType, _legacyPlanProvider.GetPlan(planType), null);
        }

        var businessPlan = await _businessCatalog.GetAsync(planType, cancellationToken);
        if (businessPlan is null || !businessPlan.Configuration.Enabled)
        {
            throw new InvalidOperationException($"Business {planType} is not configured or enabled.");
        }

        return new CommercialPlanResolution(ResolvedTenantProduct.Business, planType, null, businessPlan);
    }
}