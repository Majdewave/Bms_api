using System.Security.Claims;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Clienta.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Clienta.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Policy = "manage_invoices")]
public class PaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IFeatureService _featureService;

    public PaymentsController(AppDbContext db, ITenantContext tenantContext, IFeatureService featureService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _featureService = featureService;
    }

    [HttpGet("invoices/{invoiceId:guid}/payments")]
    public async Task<IActionResult> GetInvoicePayments(Guid invoiceId)
    {
        if (!await InvoicesEnabledAsync()) return Forbid();
        var invoice = await GetInvoiceAsync(invoiceId);
        if (invoice == null) return NotFound();
        return Ok(await BuildSummaryAsync(invoice));
    }

    [HttpGet("payments/{id:guid}")]
    public async Task<IActionResult> GetPayment(Guid id)
    {
        if (!await InvoicesEnabledAsync()) return Forbid();
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (payment == null) return NotFound();
        if (await GetInvoiceAsync(payment.InvoiceId) == null) return NotFound();
        var creator = payment.CreatedByUserId.HasValue
            ? await _db.Users.AsNoTracking()
                .Where(user => user.Id == payment.CreatedByUserId.Value && user.TenantId == payment.TenantId)
                .Select(user => new { user.FullName, user.Email })
                .FirstOrDefaultAsync()
            : null;
        return Ok(ToResponse(payment, creator?.FullName, creator?.Email));
    }

    [HttpGet("projects/{projectId:guid}/payment-summary")]
    public async Task<IActionResult> GetProjectPaymentSummary(Guid projectId)
    {
        if (!await InvoicesEnabledAsync()) return Forbid();
        if (_tenantContext.TenantId == Guid.Empty) return NotFound();
        var projectExists = await _db.Projects.AnyAsync(project => project.Id == projectId && project.TenantId == _tenantContext.TenantId);
        if (!projectExists) return NotFound();

        var invoiceRows = await _db.Invoices.AsNoTracking()
            .Where(invoice => invoice.TenantId == _tenantContext.TenantId && invoice.ProjectId == projectId && invoice.Status != InvoiceStatus.Cancelled)
            .Select(invoice => new { invoice.Id, invoice.FinalAmountToPay })
            .ToListAsync();
        return Ok(await BuildTotalsAsync(invoiceRows.Select(row => row.Id).ToList(), invoiceRows.Sum(row => row.FinalAmountToPay)));
    }

    [HttpGet("clients/{clientId:guid}/payment-summary")]
    public async Task<IActionResult> GetClientPaymentSummary(Guid clientId)
    {
        if (!await InvoicesEnabledAsync()) return Forbid();
        if (_tenantContext.TenantId == Guid.Empty) return NotFound();
        var clientExists = await _db.Clients.AnyAsync(client => client.Id == clientId && client.TenantId == _tenantContext.TenantId);
        if (!clientExists) return NotFound();

        var invoiceRows = await _db.Invoices.AsNoTracking()
            .Where(invoice => invoice.TenantId == _tenantContext.TenantId && invoice.ClientId == clientId && invoice.Status != InvoiceStatus.Cancelled)
            .Select(invoice => new { invoice.Id, invoice.FinalAmountToPay })
            .ToListAsync();
        return Ok(await BuildTotalsAsync(invoiceRows.Select(row => row.Id).ToList(), invoiceRows.Sum(row => row.FinalAmountToPay)));
    }

    [HttpPost("invoices/{invoiceId:guid}/payments")]
    public async Task<IActionResult> CreatePayment(Guid invoiceId, [FromBody] CreatePaymentRequest request)
    {
        if (!await InvoicesEnabledAsync()) return Forbid();
        var strategy = _db.Database.CreateExecutionStrategy();
        IActionResult? result = null;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            if (!await LockInvoiceRowAsync(invoiceId)) { result = NotFound("Invoice not found."); return; }
            var invoice = await GetInvoiceAsync(invoiceId);
            if (invoice == null) { result = NotFound("Invoice not found."); return; }
            if (invoice.Status == InvoiceStatus.Cancelled) { result = BadRequest("Cancelled invoices cannot receive payments."); return; }
            if (!TryParsePaymentMethod(request.PaymentMethod, out var paymentMethod)) { result = BadRequest("Unsupported payment method."); return; }
            if (request.Amount <= 0) { result = BadRequest("Payment amount must be greater than zero."); return; }

            var verifiedPaid = await GetVerifiedPaidAsync(invoice.Id);
            var remaining = RoundMoney(invoice.FinalAmountToPay - verifiedPaid);
            if (request.Amount > remaining) { result = BadRequest($"Payment exceeds the remaining balance of {remaining:0.00}."); return; }

            var userId = GetCurrentUserId();
            _db.Payments.Add(new Payment
            {
                TenantId = invoice.TenantId,
                ClientId = invoice.ClientId,
                ProjectId = invoice.ProjectId,
                InvoiceId = invoice.Id,
                Amount = RoundMoney(request.Amount),
                PaymentDate = NormalizeToUtc(request.PaymentDate) ?? DateTime.UtcNow,
                PaymentMethod = paymentMethod,
                Reference = Normalize(request.Reference),
                Notes = Normalize(request.Notes),
                Status = PaymentStatus.Verified,
                CreatedByUserId = userId,
                VerifiedByUserId = userId,
                VerifiedAt = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync();
            await SynchronizeInvoiceStatusAsync(invoice);
            await _db.SaveChangesAsync();
            result = Ok(await BuildSummaryAsync(invoice));
            await transaction.CommitAsync();
        });
        return result ?? StatusCode(StatusCodes.Status500InternalServerError);
    }

    [HttpPut("payments/{id:guid}")]
    public async Task<IActionResult> UpdatePayment(Guid id, [FromBody] UpdatePaymentRequest request)
    {
        if (!await InvoicesEnabledAsync()) return Forbid();
        var invoiceId = await _db.Payments.Where(item => item.Id == id).Select(item => (Guid?)item.InvoiceId).FirstOrDefaultAsync();
        if (!invoiceId.HasValue) return NotFound();
        var strategy = _db.Database.CreateExecutionStrategy();
        IActionResult? result = null;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            if (!await LockInvoiceRowAsync(invoiceId.Value)) { result = NotFound(); return; }
            var payment = await _db.Payments.FirstOrDefaultAsync(item => item.Id == id && item.InvoiceId == invoiceId.Value);
            if (payment == null) { result = NotFound(); return; }
            if (payment.Status != PaymentStatus.Verified) { result = BadRequest("Only verified payments can be edited."); return; }
            if (!TryParsePaymentMethod(request.PaymentMethod, out var paymentMethod)) { result = BadRequest("Unsupported payment method."); return; }
            if (request.Amount <= 0) { result = BadRequest("Payment amount must be greater than zero."); return; }

            var invoice = await GetInvoiceAsync(payment.InvoiceId);
            if (invoice == null) { result = NotFound("Invoice not found."); return; }
            if (invoice.Status == InvoiceStatus.Cancelled) { result = BadRequest("Cancelled invoices cannot receive payments."); return; }

            var otherVerified = await _db.Payments
                .Where(item => item.InvoiceId == invoice.Id && item.Id != id && item.Status == PaymentStatus.Verified)
                .SumAsync(item => (decimal?)item.Amount) ?? 0m;
            var remainingForEdit = RoundMoney(invoice.FinalAmountToPay - otherVerified);
            if (request.Amount > remainingForEdit) { result = BadRequest($"Payment exceeds the remaining balance of {remainingForEdit:0.00}."); return; }

            payment.Amount = RoundMoney(request.Amount);
            payment.PaymentDate = NormalizeToUtc(request.PaymentDate) ?? payment.PaymentDate;
            payment.PaymentMethod = paymentMethod;
            payment.Reference = Normalize(request.Reference);
            payment.Notes = Normalize(request.Notes);
            payment.UpdatedAt = DateTime.UtcNow;
            await SynchronizeInvoiceStatusAsync(invoice);
            await _db.SaveChangesAsync();
            result = Ok(await BuildSummaryAsync(invoice));
            await transaction.CommitAsync();
        });
        return result ?? StatusCode(StatusCodes.Status500InternalServerError);
    }

    [HttpPost("payments/{id:guid}/cancel")]
    public async Task<IActionResult> CancelPayment(Guid id)
    {
        if (!await InvoicesEnabledAsync()) return Forbid();
        var invoiceId = await _db.Payments.Where(item => item.Id == id).Select(item => (Guid?)item.InvoiceId).FirstOrDefaultAsync();
        if (!invoiceId.HasValue) return NotFound();
        var strategy = _db.Database.CreateExecutionStrategy();
        IActionResult? result = null;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            if (!await LockInvoiceRowAsync(invoiceId.Value)) { result = NotFound(); return; }
            var payment = await _db.Payments.FirstOrDefaultAsync(item => item.Id == id && item.InvoiceId == invoiceId.Value);
            if (payment == null) { result = NotFound(); return; }
            var invoice = await GetInvoiceAsync(invoiceId.Value);
            if (invoice == null) { result = NotFound("Invoice not found."); return; }

            payment.Status = PaymentStatus.Cancelled;
            payment.UpdatedAt = DateTime.UtcNow;
            await SynchronizeInvoiceStatusAsync(invoice);
            await _db.SaveChangesAsync();
            result = Ok(await BuildSummaryAsync(invoice));
            await transaction.CommitAsync();
        });
        return result ?? StatusCode(StatusCodes.Status500InternalServerError);
    }

    private Task<bool> InvoicesEnabledAsync() => _featureService.IsEnabledAsync("invoices");

    // Serialize all mutations for one invoice using PostgreSQL's row-level FOR UPDATE lock.
    // The lock is held by the EF transaction through validation, write, and status synchronization.
    private async Task<bool> LockInvoiceRowAsync(Guid invoiceId)
    {
        if (_tenantContext.TenantId == Guid.Empty) return false;
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT \"Id\" FROM \"Invoices\" WHERE \"Id\" = @invoiceId AND \"TenantId\" = @tenantId FOR UPDATE";
        command.Parameters.AddWithValue("invoiceId", invoiceId);
        command.Parameters.AddWithValue("tenantId", _tenantContext.TenantId);
        return await command.ExecuteScalarAsync() != null;
    }

    private async Task<Invoice?> GetInvoiceAsync(Guid invoiceId) =>
        await _db.Invoices.FirstOrDefaultAsync(invoice => invoice.Id == invoiceId && invoice.TenantId == _tenantContext.TenantId);

    private async Task<InvoicePaymentSummaryResponse> BuildSummaryAsync(Invoice invoice)
    {
        var payments = await _db.Payments.AsNoTracking()
            .Where(item => item.InvoiceId == invoice.Id)
            .OrderByDescending(item => item.PaymentDate)
            .ThenByDescending(item => item.CreatedAt)
            .ToListAsync();
        var creatorIds = payments
            .Where(item => item.CreatedByUserId.HasValue)
            .Select(item => item.CreatedByUserId!.Value)
            .Distinct()
            .ToArray();
        var creators = await _db.Users.AsNoTracking()
            .Where(user => user.TenantId == invoice.TenantId && creatorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName, user.Email })
            .ToDictionaryAsync(user => user.Id);
        var paid = RoundMoney(payments.Where(item => item.Status == PaymentStatus.Verified).Sum(item => item.Amount));
        var remaining = RoundMoney(Math.Max(0, invoice.FinalAmountToPay - paid));
        var paymentResponses = payments.Select(item =>
        {
            creators.TryGetValue(item.CreatedByUserId ?? Guid.Empty, out var creator);
            return ToResponse(item, creator?.FullName, creator?.Email);
        }).ToList();
        return new InvoicePaymentSummaryResponse(invoice.FinalAmountToPay, paid, remaining, GetPaymentState(paid, remaining), paymentResponses);
    }

    private async Task<decimal> GetVerifiedPaidAsync(Guid invoiceId) =>
        RoundMoney(await _db.Payments.Where(item => item.InvoiceId == invoiceId && item.Status == PaymentStatus.Verified).SumAsync(item => (decimal?)item.Amount) ?? 0m);

    private async Task<PaymentTotalsResponse> BuildTotalsAsync(IReadOnlyList<Guid> invoiceIds, decimal invoiceAmountDue)
    {
        var paid = invoiceIds.Count == 0 ? 0m : RoundMoney(await _db.Payments
            .Where(payment => payment.TenantId == _tenantContext.TenantId && invoiceIds.Contains(payment.InvoiceId) && payment.Status == PaymentStatus.Verified)
            .SumAsync(payment => (decimal?)payment.Amount) ?? 0m);
        return new PaymentTotalsResponse(invoiceAmountDue, paid, RoundMoney(Math.Max(0m, invoiceAmountDue - paid)));
    }

    private async Task SynchronizeInvoiceStatusAsync(Invoice invoice)
    {
        if (invoice.Status == InvoiceStatus.Cancelled) return;
        var paid = await GetVerifiedPaidAsync(invoice.Id);
        var remaining = RoundMoney(invoice.FinalAmountToPay - paid);
        invoice.Status = paid == 0 ? InvoiceStatus.Pending : remaining <= 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
    }

    private static string GetPaymentState(decimal paid, decimal remaining) => paid == 0 ? "pending" : remaining <= 0 ? "paid" : "partially_paid";
    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime? NormalizeToUtc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    private Guid? GetCurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId) ? userId : null;

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

    private static PaymentResponse ToResponse(Payment payment, string? createdByName, string? createdByEmail) => new(
        payment.Id, payment.InvoiceId, payment.ClientId, payment.ProjectId, payment.Amount, payment.PaymentDate,
        payment.PaymentMethod.ToString(), payment.Reference, payment.Notes, payment.Status, payment.CreatedAt,
        payment.UpdatedAt, payment.CreatedByUserId, payment.VerifiedByUserId, payment.VerifiedAt, createdByName, createdByEmail);
}
