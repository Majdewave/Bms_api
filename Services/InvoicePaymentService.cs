using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Clienta.Api.Services;

public enum CreateInvoicePaymentOutcome
{
    Created,
    InvoiceNotFound,
    CancelledInvoice,
    UnsupportedPaymentMethod,
    InvalidAmount,
    ExceedsRemainingBalance
}

public sealed record CreateInvoicePaymentResult(
    CreateInvoicePaymentOutcome Outcome,
    InvoicePaymentSummaryResponse? Summary = null,
    decimal? RemainingBalance = null);

public interface IInvoicePaymentService
{
    Task<CreateInvoicePaymentResult> CreateAsync(Guid invoiceId, CreatePaymentRequest request, Guid? userId, Guid? excludeReservationId = null, bool transactionAlreadyActive = false);
}

/// <summary>
/// Owns the canonical CLIENTA invoice-payment transaction. This service deliberately has no
/// accounting-provider dependency: native payment recording must remain provider-independent.
/// </summary>
public sealed class InvoicePaymentService(AppDbContext db, ITenantContext tenantContext, IInvoiceBalanceService? balanceService = null) : IInvoicePaymentService
{
    private readonly IInvoiceBalanceService _balanceService = balanceService ?? new InvoiceBalanceService(db);

    public async Task<CreateInvoicePaymentResult> CreateAsync(Guid invoiceId, CreatePaymentRequest request, Guid? userId, Guid? excludeReservationId = null, bool transactionAlreadyActive = false)
    {
        CreateInvoicePaymentResult? result = null;
        var paymentId = Guid.NewGuid();
        async Task ExecuteInTransactionAsync()
        {
            await using var transaction = transactionAlreadyActive ? null : await db.Database.BeginTransactionAsync();
            try
            {
            if (!await LockInvoiceRowAsync(invoiceId))
            {
                result = new(CreateInvoicePaymentOutcome.InvoiceNotFound);
                return;
            }

            var invoice = await db.Invoices.FirstOrDefaultAsync(item =>
                item.Id == invoiceId && item.TenantId == tenantContext.TenantId);
            if (invoice == null)
            {
                result = new(CreateInvoicePaymentOutcome.InvoiceNotFound);
                return;
            }
            if (invoice.Status == InvoiceStatus.Cancelled)
            {
                result = new(CreateInvoicePaymentOutcome.CancelledInvoice);
                return;
            }
            if (!TryParsePaymentMethod(request.PaymentMethod, out var paymentMethod))
            {
                result = new(CreateInvoicePaymentOutcome.UnsupportedPaymentMethod);
                return;
            }
            if (request.Amount <= 0)
            {
                result = new(CreateInvoicePaymentOutcome.InvalidAmount);
                return;
            }

            if (!string.IsNullOrWhiteSpace(request.Reference))
            {
                var existing = await db.Payments.FirstOrDefaultAsync(item => item.TenantId == invoice.TenantId
                    && item.InvoiceId == invoice.Id && item.Reference == request.Reference.Trim()
                    && item.Status == PaymentStatus.Verified);
                if (existing is not null)
                {
                    if (existing.Amount != RoundMoney(request.Amount) || existing.PaymentMethod != paymentMethod)
                    {
                        result = new(CreateInvoicePaymentOutcome.InvalidAmount,
                            RemainingBalance: await _balanceService.GetAvailableAsync(invoice.TenantId, invoice.Id, excludeReservationId));
                        return;
                    }
                    var existingSummary = await BuildSummaryAsync(invoice);
                    if (transaction is not null) await transaction.CommitAsync();
                    result = new(CreateInvoicePaymentOutcome.Created, existingSummary);
                    return;
                }
            }

            // Keep one identity across execution-strategy retries. If the previous attempt committed
            // but its acknowledgement was lost, the retry returns that payment instead of inserting another.
            var retriedPayment = await db.Payments.FirstOrDefaultAsync(item => item.Id == paymentId
                && item.TenantId == invoice.TenantId && item.InvoiceId == invoice.Id);
            if (retriedPayment is not null)
            {
                var retriedSummary = await BuildSummaryAsync(invoice);
                if (transaction is not null) await transaction.CommitAsync();
                result = new(CreateInvoicePaymentOutcome.Created, retriedSummary);
                return;
            }

            var remaining = await _balanceService.GetAvailableAsync(invoice.TenantId, invoice.Id, excludeReservationId);
            if (request.Amount > remaining)
            {
                result = new(CreateInvoicePaymentOutcome.ExceedsRemainingBalance, RemainingBalance: remaining);
                return;
            }

            var now = DateTime.UtcNow;
            db.Payments.Add(new Payment
            {
                Id = paymentId,
                TenantId = invoice.TenantId,
                ClientId = invoice.ClientId,
                ProjectId = invoice.ProjectId,
                InvoiceId = invoice.Id,
                Amount = RoundMoney(request.Amount),
                PaymentDate = NormalizeToUtc(request.PaymentDate) ?? now,
                PaymentMethod = paymentMethod,
                Reference = Normalize(request.Reference),
                Notes = Normalize(request.Notes),
                Status = PaymentStatus.Verified,
                CreatedByUserId = userId,
                VerifiedByUserId = userId,
                VerifiedAt = now,
            });

            await db.SaveChangesAsync();
            await SynchronizeInvoiceStatusAsync(invoice);
            await db.SaveChangesAsync();
            var summary = await BuildSummaryAsync(invoice);
            if (transaction is not null) await transaction.CommitAsync();
            result = new(CreateInvoicePaymentOutcome.Created, summary);
            }
            catch
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync();
                    db.ChangeTracker.Clear();
                }
                throw;
            }
        }

        if (transactionAlreadyActive)
        {
            // The caller owns the transaction and its execution strategy; avoid nesting a strategy
            // around an already-active transaction.
            await ExecuteInTransactionAsync();
        }
        else
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(ExecuteInTransactionAsync);
        }

        return result ?? throw new InvalidOperationException("Payment creation completed without a result.");
    }

    // PostgreSQL serializes payment mutations per invoice. SQLite-backed tests use a transaction
    // without FOR UPDATE; production behavior remains the PostgreSQL row lock.
    private async Task<bool> LockInvoiceRowAsync(Guid invoiceId)
    {
        if (tenantContext.TenantId == Guid.Empty) return false;
        if (!db.Database.IsNpgsql())
            return await db.Invoices.AnyAsync(invoice => invoice.Id == invoiceId && invoice.TenantId == tenantContext.TenantId);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT \"Id\" FROM \"Invoices\" WHERE \"Id\" = @invoiceId AND \"TenantId\" = @tenantId FOR UPDATE";
        command.Parameters.AddWithValue("invoiceId", invoiceId);
        command.Parameters.AddWithValue("tenantId", tenantContext.TenantId);
        return await command.ExecuteScalarAsync() != null;
    }

    private async Task SynchronizeInvoiceStatusAsync(Invoice invoice)
    {
        if (invoice.Status == InvoiceStatus.Cancelled) return;
        var payments = db.Payments.Where(item => item.TenantId == invoice.TenantId && item.InvoiceId == invoice.Id && item.Status == PaymentStatus.Verified);
        var paid = db.Database.IsSqlite()
            ? RoundMoney((await payments.Select(item => item.Amount).ToListAsync()).Sum())
            : RoundMoney(await payments.Select(item => (decimal?)item.Amount).SumAsync() ?? 0m);
        var remaining = RoundMoney(invoice.FinalAmountToPay - paid);
        invoice.Status = paid == 0 ? InvoiceStatus.Pending : remaining <= 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
    }

    private async Task<InvoicePaymentSummaryResponse> BuildSummaryAsync(Invoice invoice)
    {
        var payments = await db.Payments.AsNoTracking()
            .Where(item => item.InvoiceId == invoice.Id)
            .OrderByDescending(item => item.PaymentDate)
            .ThenByDescending(item => item.CreatedAt)
            .ToListAsync();
        var creatorIds = payments
            .Where(item => item.CreatedByUserId.HasValue)
            .Select(item => item.CreatedByUserId!.Value)
            .Distinct()
            .ToArray();
        var creators = await db.Users.AsNoTracking()
            .Where(user => user.TenantId == invoice.TenantId && creatorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName, user.Email })
            .ToDictionaryAsync(user => user.Id);
        var paid = RoundMoney(payments.Where(item => item.Status == PaymentStatus.Verified).Sum(item => item.Amount));
        var remaining = RoundMoney(Math.Max(0, invoice.FinalAmountToPay - paid));
        var responses = payments.Select(item =>
        {
            creators.TryGetValue(item.CreatedByUserId ?? Guid.Empty, out var creator);
            return new PaymentResponse(
                item.Id, item.InvoiceId, item.ClientId, item.ProjectId, item.Amount, item.PaymentDate,
                item.PaymentMethod.ToString(), item.Reference, item.Notes, item.Status, item.CreatedAt,
                item.UpdatedAt, item.CreatedByUserId, item.VerifiedByUserId, item.VerifiedAt,
                creator?.FullName, creator?.Email);
        }).ToList();
        return new InvoicePaymentSummaryResponse(invoice.FinalAmountToPay, paid, remaining,
            paid == 0 ? "pending" : remaining <= 0 ? "paid" : "partially_paid", responses);
    }

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime? NormalizeToUtc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private static bool TryParsePaymentMethod(string value, out PaymentMethod paymentMethod)
    {
        paymentMethod = value.Trim().ToLowerInvariant() switch
        {
            "cash" => PaymentMethod.Cash,
            "credit" or "credit_card" => PaymentMethod.Credit,
            "bank_transfer" => PaymentMethod.BankTransfer,
            "check" or "cheque" => PaymentMethod.Check,
            "bit" => PaymentMethod.Bit,
            "paybox" => PaymentMethod.PayBox,
            "other" => PaymentMethod.Other,
            _ => (PaymentMethod)(-1),
        };
        return (int)paymentMethod >= 0;
    }
}
