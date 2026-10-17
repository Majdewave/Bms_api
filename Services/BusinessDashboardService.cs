using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IBusinessDashboardService
{
    Task<bool> IsInvoicesEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<BusinessDashboardResponse> GetAsync(Guid tenantId, bool includeInvoiceFinancials, DateTime? utcNow = null, CancellationToken cancellationToken = default);
}

// Read-only aggregates for the Business dashboard. Every query is scoped by the supplied tenant id.
// Included projects: Active, OnHold, Completed (Draft and Cancelled are excluded from every metric).
public class BusinessDashboardService : IBusinessDashboardService
{
    private readonly AppDbContext _db;

    public BusinessDashboardService(AppDbContext db)
    {
        _db = db;
    }

    // Reads the flag directly: FeatureService.GetAsync would create a TenantFeatures row, and this path must stay read-only.
    public async Task<bool> IsInvoicesEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        tenantId != Guid.Empty &&
        (await _db.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => BusinessPlanEntitlement.HasCurrentBusinessCapabilities(tenant.ProductType, tenant.Plan))
            .FirstOrDefaultAsync(cancellationToken)
         || await _db.TenantFeatures.AsNoTracking()
            .Where(features => features.TenantId == tenantId)
            .Select(features => features.InvoicesEnabled)
            .FirstOrDefaultAsync(cancellationToken));

    public async Task<BusinessDashboardResponse> GetAsync(Guid tenantId, bool includeInvoiceFinancials, DateTime? utcNow = null, CancellationToken cancellationToken = default)
    {
        var today = (utcNow ?? DateTime.UtcNow).Date;

        var currency = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Currency)
            .FirstOrDefaultAsync(cancellationToken) ?? "ILS";

        var projects = _db.Projects.AsNoTracking().Where(p =>
            p.TenantId == tenantId &&
            (p.Status == ProjectStatus.Active || p.Status == ProjectStatus.OnHold || p.Status == ProjectStatus.Completed));
        var projectIds = projects.Select(p => p.Id);

        var projectCount = await projects.CountAsync(cancellationToken);
        var projectValue = await projects.SumAsync(p => (decimal?)p.Value, cancellationToken) ?? 0m;

        // Same plan semantics as the PM billing plan page: planned/ready-to-invoice milestones are "not yet invoiced".
        var openMilestones = _db.BillingMilestones.AsNoTracking().Where(m =>
            m.TenantId == tenantId &&
            (m.Status == BillingMilestoneStatus.Planned || m.Status == BillingMilestoneStatus.ReadyToInvoice) &&
            projectIds.Contains(m.ProjectId));
        var notYetInvoiced = await openMilestones.SumAsync(m => (decimal?)m.Amount, cancellationToken) ?? 0m;

        decimal? invoiced = null, paid = null, outstanding = null;
        if (includeInvoiceFinancials)
        {
            // Same rule as the existing project/client payment summaries: non-cancelled invoices, Verified payments only.
            var invoices = _db.Invoices.AsNoTracking().Where(i =>
                i.TenantId == tenantId &&
                i.Status != InvoiceStatus.Cancelled &&
                i.ProjectId != null &&
                projectIds.Contains(i.ProjectId.Value));
            var invoiceIds = invoices.Select(i => i.Id);

            var invoicedTotal = await invoices.SumAsync(i => (decimal?)i.FinalAmountToPay, cancellationToken) ?? 0m;
            var paidTotal = await _db.Payments.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.Status == PaymentStatus.Verified && invoiceIds.Contains(p.InvoiceId))
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

            invoiced = Round(invoicedTotal);
            paid = Round(paidTotal);
            outstanding = Round(Math.Max(0m, invoicedTotal - paidTotal));
        }

        // Earliest open milestone with a due date (overdue ones included); milestones without a date are never given one.
        var next = await openMilestones
            .Where(m => m.DueDate != null)
            .OrderBy(m => m.DueDate).ThenBy(m => m.SortOrder).ThenBy(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id,
                m.Title,
                m.Amount,
                DueDate = m.DueDate!.Value,
                m.ProjectId,
                ProjectName = m.Project.Name,
                ClientId = m.Project.ClientId,
                ClientName = m.Project.Client.FullName
            })
            .FirstOrDefaultAsync(cancellationToken);

        BusinessNextBillingDto? nextBilling = null;
        if (next != null)
        {
            // Same pick as the PM overview: first billing contact, primary first.
            var contact = await _db.ClientContacts.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.ClientId == next.ClientId && c.IsBillingContact)
                .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.CreatedAt)
                .Select(c => new BusinessBillingContactDto(c.Name, c.RoleTitle, c.Phone, c.WhatsApp, c.Email))
                .FirstOrDefaultAsync(cancellationToken);

            nextBilling = new BusinessNextBillingDto(
                next.ProjectId, next.ProjectName, next.ClientId, next.ClientName,
                next.Id, next.Title, Round(next.Amount), next.DueDate, next.DueDate.Date < today, contact);
        }

        return new BusinessDashboardResponse(
            currency, projectCount, Round(projectValue), includeInvoiceFinancials,
            invoiced, paid, outstanding, Round(notYetInvoiced), nextBilling);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
