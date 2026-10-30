using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public interface IInvoiceBalanceService
{
    Task<decimal> GetAvailableAsync(Guid tenantId, Guid invoiceId, Guid? excludeOperationId = null, CancellationToken cancellationToken = default, Guid? excludePaymentId = null);
}

/// <summary>Shared availability calculation: verified CLIENTA payments plus active/uncertain external receipt reservations.</summary>
public sealed class InvoiceBalanceService(AppDbContext db) : IInvoiceBalanceService
{
    public async Task<decimal> GetAvailableAsync(Guid tenantId, Guid invoiceId, Guid? excludeOperationId = null, CancellationToken cancellationToken = default, Guid? excludePaymentId = null)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Id == invoiceId)
            .Select(item => (decimal?)item.FinalAmountToPay)
            .FirstOrDefaultAsync(cancellationToken);
        if (!invoice.HasValue) return 0m;

        var verifiedQuery = db.Payments.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.InvoiceId == invoiceId && item.Status == PaymentStatus.Verified);
        if (excludePaymentId.HasValue) verifiedQuery = verifiedQuery.Where(item => item.Id != excludePaymentId.Value);
        var verified = await verifiedQuery
            .Select(item => (decimal?)item.Amount)
            .ToListAsync(cancellationToken);
        var reservedQuery = db.ExternalAccountingOperations.AsNoTracking().Where(item =>
            item.TenantId == tenantId && item.InvoiceId == invoiceId
            && !item.PaymentId.HasValue
            && (item.Status == ExternalAccountingOperationStatus.Issuing
                || item.Status == ExternalAccountingOperationStatus.Finalizing
                || item.Status == ExternalAccountingOperationStatus.ReconciliationRequired));
        if (excludeOperationId.HasValue) reservedQuery = reservedQuery.Where(item => item.Id != excludeOperationId.Value);
        var reserved = await reservedQuery.Select(item => (decimal?)item.Amount).ToListAsync(cancellationToken);
        var committedAndReserved = verified.Sum(value => value ?? 0m) + reserved.Sum(value => value ?? 0m);
        return Math.Round(Math.Max(0m, invoice.Value - committedAndReserved), 2, MidpointRounding.AwayFromZero);
    }
}
