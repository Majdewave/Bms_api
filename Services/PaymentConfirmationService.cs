using System.Security.Claims;
using Clienta.Api.Data;
using Clienta.Api.DTOs;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Clienta.Api.Services;

public sealed class PaymentConfirmationService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPaymentConfirmationAccessService _access;
    private readonly IPaymentConfirmationTokenService _tokens;
    private readonly IPaymentConfirmationProofStorage _proofStorage;
    private readonly IConfiguration _configuration;

    public PaymentConfirmationService(
        AppDbContext db,
        ITenantContext tenantContext,
        IPaymentConfirmationAccessService access,
        IPaymentConfirmationTokenService tokens,
        IPaymentConfirmationProofStorage proofStorage,
        IConfiguration configuration)
    {
        _db = db;
        _tenantContext = tenantContext;
        _access = access;
        _tokens = tokens;
        _proofStorage = proofStorage;
        _configuration = configuration;
    }

    public async Task<PaymentConfirmationCreatedResponse> CreateAsync(
        Guid tenantId,
        Guid? userId,
        Guid invoiceId,
        CreatePaymentConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("Tenant context is not available.");
        if (request.RequestedAmount is null or <= 0)
            throw new InvalidOperationException("Requested amount must be greater than zero.");
        var expiresInDays = request.ExpiresInDays ?? 7;
        if (expiresInDays is < 1 or > 30)
            throw new InvalidOperationException("Expiry must be between 1 and 30 days.");

        var strategy = _db.Database.CreateExecutionStrategy();
        PaymentConfirmationCreatedResponse? result = null;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            if (!await LockInvoiceAsync(tenantId, invoiceId, cancellationToken))
                throw new KeyNotFoundException("Invoice not found.");

            var invoice = await _db.Invoices.FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.Id == invoiceId,
                cancellationToken)
                ?? throw new KeyNotFoundException("Invoice not found.");
            if (invoice.Status == InvoiceStatus.Cancelled)
                throw new InvalidOperationException("Cancelled invoices cannot receive payment confirmations.");

            var remaining = await GetRemainingAsync(tenantId, invoice, cancellationToken);
            if (remaining <= 0)
                throw new InvalidOperationException("This invoice is already fully paid.");
            if (request.RequestedAmount.Value > remaining)
                throw new InvalidOperationException($"Requested amount cannot exceed the current remaining balance of {remaining:0.00}.");

            var tenant = await _db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken)
                ?? throw new KeyNotFoundException("Tenant not found.");

            var activeRequests = await _db.PaymentConfirmationRequests
                .Where(item => item.TenantId == tenantId
                    && item.InvoiceId == invoiceId
                    && item.Status == PaymentConfirmationRequestStatus.Active)
                .ToListAsync(cancellationToken);
            var revokedAt = DateTime.UtcNow;
            foreach (var activeRequest in activeRequests)
            {
                activeRequest.Status = PaymentConfirmationRequestStatus.Revoked;
                activeRequest.RevokedAt = revokedAt;
            }

            var token = _tokens.Create();
            var confirmation = new PaymentConfirmationRequest
            {
                TenantId = tenantId,
                InvoiceId = invoice.Id,
                ClientId = invoice.ClientId,
                ProjectId = invoice.ProjectId,
                TokenHash = token.TokenHash,
                RequestedAmount = request.RequestedAmount.Value,
                CurrencyCode = NormalizeCurrency(tenant.Currency),
                ExpiresAt = DateTime.UtcNow.AddDays(expiresInDays),
                CreatedByUserId = userId,
            };

            _db.PaymentConfirmationRequests.Add(confirmation);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var baseUrl = (_configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
            var publicUrl = $"{baseUrl}/payment-confirmation/{Uri.EscapeDataString(token.RawToken)}";
            result = new PaymentConfirmationCreatedResponse(
                confirmation.Id,
                token.RawToken,
                publicUrl,
                confirmation.RequestedAmount,
                confirmation.CurrencyCode,
                confirmation.ExpiresAt);
        });

        return result ?? throw new InvalidOperationException("Could not create payment confirmation request.");
    }

    public async Task<PaymentConfirmationReviewResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var request = await _db.PaymentConfirmationRequests.AsNoTracking()
            .Include(item => item.Invoice)
            .Include(item => item.Client)
            .Include(item => item.SubmittedPayment)
            .Include(item => item.Proof)
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == id, cancellationToken);
        return request == null ? null : ToReview(request);
    }

    public async Task<IReadOnlyList<PaymentConfirmationReviewResponse>?> GetByInvoiceAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoiceExists = await _db.Invoices.AsNoTracking()
            .AnyAsync(item => item.TenantId == tenantId && item.Id == invoiceId, cancellationToken);
        if (!invoiceExists) return null;

        var requests = await _db.PaymentConfirmationRequests.AsNoTracking()
            .Include(item => item.Invoice)
            .Include(item => item.Client)
            .Include(item => item.Project)
            .Include(item => item.SubmittedPayment)
            .Include(item => item.Proof)
            .Where(item => item.TenantId == tenantId && item.InvoiceId == invoiceId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return requests.Select(ToReview).ToList();
    }

    public async Task<PaymentConfirmationProofReadResult?> OpenProofAsync(Guid tenantId, Guid requestId, CancellationToken cancellationToken)
    {
        var proof = await _db.PaymentConfirmationProofs.AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.PaymentConfirmationRequestId == requestId, cancellationToken);
        if (proof == null) return null;

        var requestExists = await _db.PaymentConfirmationRequests.AsNoTracking()
            .AnyAsync(item => item.TenantId == tenantId && item.Id == requestId, cancellationToken);
        if (!requestExists) return null;

        return await _proofStorage.OpenReadAsync(proof.StorageKey, proof.OriginalFileName, proof.ContentType, cancellationToken);
    }

    public async Task<bool> RevokeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var request = await _db.PaymentConfirmationRequests
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == id, cancellationToken);
        if (request == null) return false;
        if (request.Status != PaymentConfirmationRequestStatus.Active)
            throw new InvalidOperationException("Only active requests can be revoked.");

        request.Status = PaymentConfirmationRequestStatus.Revoked;
        request.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PublicPaymentConfirmationResponse?> GetPublicAsync(string rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;
        var tokenHash = _tokens.Hash(rawToken);
        var request = await _db.PaymentConfirmationRequests.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (request == null || !await _access.IsEnabledAsync(request.TenantId, cancellationToken)) return null;
        if (request.Status == PaymentConfirmationRequestStatus.Revoked || request.ExpiresAt <= DateTime.UtcNow)
            return null;

        var invoice = await _db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.InvoiceId && item.TenantId == request.TenantId, cancellationToken);
        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.TenantId, cancellationToken);
        if (invoice == null || tenant == null) return null;

        var remaining = await GetRemainingAsync(request.TenantId, invoice, cancellationToken);
        var projectName = invoice.ProjectId.HasValue
            ? await _db.Projects.AsNoTracking()
                .Where(project => project.Id == invoice.ProjectId.Value && project.TenantId == request.TenantId)
                .Select(project => project.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var descriptions = await _db.InvoiceLineItems.AsNoTracking()
            .Where(line => line.InvoiceId == invoice.Id)
            .OrderBy(line => line.Id)
            .Select(line => line.Description)
            .ToListAsync(cancellationToken);
        var paymentDescriptions = descriptions
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .Select(description => description.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (paymentDescriptions.Count == 0)
        {
            var milestoneSummary = await _db.BillingMilestones.AsNoTracking()
                .Where(milestone => milestone.TenantId == request.TenantId && milestone.InvoiceId == invoice.Id)
                .OrderBy(milestone => milestone.SortOrder)
                .Select(milestone => milestone.Title)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(milestoneSummary))
                paymentDescriptions.Add(milestoneSummary.Trim());
        }
        if (paymentDescriptions.Count == 0 && !string.IsNullOrWhiteSpace(projectName))
            paymentDescriptions.Add(projectName.Trim());
        if (paymentDescriptions.Count == 0)
            paymentDescriptions.Add(invoice.InvoiceNumber);
        paymentDescriptions = paymentDescriptions
            .Select(description => description.Length <= 240 ? description : description[..240].TrimEnd() + "…")
            .ToList();

        var requestState = request.Status == PaymentConfirmationRequestStatus.Active && remaining <= 0
            ? "Unavailable"
            : request.Status.ToString();
        var submissionState = request.Status == PaymentConfirmationRequestStatus.Submitted
            ? request.SubmittedPaymentId.HasValue
                ? await _db.Payments.AsNoTracking()
                    .Where(payment => payment.Id == request.SubmittedPaymentId.Value && payment.TenantId == request.TenantId)
                    .Select(payment => payment.Status == PaymentStatus.PendingVerification ? "PendingReview" : "Processed")
                    .FirstOrDefaultAsync(cancellationToken) ?? "Processed"
                : "Processed"
            : null;
        return new PublicPaymentConfirmationResponse(
            ResolveBusinessName(tenant, invoice),
            tenant.LogoUrl ?? invoice.LogoUrl,
            invoice.InvoiceNumber,
            projectName,
            paymentDescriptions,
            request.RequestedAmount,
            remaining,
            request.CurrencyCode,
            request.ExpiresAt,
            requestState,
            submissionState,
            NormalizeLanguage(tenant.PreferredLanguage));
    }

    public async Task<PublicPaymentConfirmationSubmissionResponse> SubmitAsync(
        string rawToken,
        SubmitPaymentConfirmationRequest submission,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw new KeyNotFoundException("Request not found.");
        if (submission.AmountPaid <= 0) throw new InvalidOperationException("Amount must be greater than zero.");

        var strategy = _db.Database.CreateExecutionStrategy();
        PublicPaymentConfirmationSubmissionResponse? result = null;
        string? uploadedKey = null;
        var transactionCommitted = false;
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                try
                {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var tokenHash = _tokens.Hash(rawToken);
            var requestId = await LockRequestByTokenHashAsync(tokenHash, cancellationToken);
            if (!requestId.HasValue) throw new KeyNotFoundException("Request not found.");

            var request = await _db.PaymentConfirmationRequests.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == requestId.Value && item.TokenHash == tokenHash, cancellationToken)
                ?? throw new KeyNotFoundException("Request not found.");
            if (!await _access.IsEnabledAsync(request.TenantId, cancellationToken)) throw new KeyNotFoundException("Request not found.");
            if (request.Status != PaymentConfirmationRequestStatus.Active || request.ExpiresAt <= DateTime.UtcNow)
                throw new InvalidOperationException("This payment confirmation request is no longer active.");

            _tenantContext.SetTenant(request.TenantId);
            var invoice = await _db.Invoices
                .FirstOrDefaultAsync(item => item.Id == request.InvoiceId && item.TenantId == request.TenantId, cancellationToken)
                ?? throw new KeyNotFoundException("Request not found.");
            if (invoice.Status == InvoiceStatus.Cancelled) throw new InvalidOperationException("This invoice cannot receive payment confirmations.");

            var remaining = await GetRemainingAsync(request.TenantId, invoice, cancellationToken);
            if (submission.AmountPaid > remaining) throw new InvalidOperationException("Amount exceeds the current remaining balance.");

            PaymentConfirmationProofUpload? proofUpload = null;
            if (submission.ProofFile != null)
            {
                proofUpload = await _proofStorage.UploadAsync(request.TenantId, request.Id, submission.ProofFile, cancellationToken);
                uploadedKey = proofUpload.StorageKey;
            }

            var payment = new Payment
            {
                TenantId = request.TenantId,
                ClientId = request.ClientId,
                ProjectId = request.ProjectId,
                InvoiceId = request.InvoiceId,
                Amount = RoundMoney(submission.AmountPaid),
                PaymentDate = NormalizeToUtc(submission.PaymentDate),
                PaymentMethod = PaymentMethod.BankTransfer,
                Reference = Normalize(submission.Reference),
                Status = PaymentStatus.PendingVerification,
            };
            _db.Payments.Add(payment);
            request.Status = PaymentConfirmationRequestStatus.Submitted;
            var submittedAt = DateTime.UtcNow;
            request.SubmittedAt = submittedAt;
            request.SubmittedPaymentId = payment.Id;
            if (proofUpload != null)
            {
                _db.PaymentConfirmationProofs.Add(new PaymentConfirmationProof
                {
                    TenantId = request.TenantId,
                    PaymentConfirmationRequestId = request.Id,
                    StorageKey = proofUpload.StorageKey,
                    OriginalFileName = proofUpload.OriginalFileName,
                    ContentType = proofUpload.ContentType,
                    SizeBytes = proofUpload.SizeBytes,
                    Sha256 = proofUpload.Sha256,
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
                transactionCommitted = true;
            result = new PublicPaymentConfirmationSubmissionResponse(payment.Amount, submittedAt, "PendingReview");
                }
                catch
                {
                    if (uploadedKey != null && !transactionCommitted)
                    {
                        await CleanupUploadedProofAsync(uploadedKey, cancellationToken);
                        uploadedKey = null;
                    }

                    throw;
                }
            });
        }
        catch
        {
            if (uploadedKey != null && !transactionCommitted)
            {
                await CleanupUploadedProofAsync(uploadedKey, cancellationToken);
            }

            throw;
        }

        if (result == null)
            throw new InvalidOperationException("Could not submit payment confirmation.");

        return result;
    }

    private async Task CleanupUploadedProofAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await _proofStorage.DeleteAsync(storageKey, cancellationToken);
        }
        catch (Exception cleanupError)
        {
            Console.Error.WriteLine($"Payment confirmation proof cleanup failed: {cleanupError.GetType().Name}");
        }
    }

    public async Task<bool> VerifyAsync(Guid tenantId, Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        var verified = false;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            if (!await LockRequestAsync(tenantId, id, cancellationToken)) throw new KeyNotFoundException("Request not found.");
            var request = await _db.PaymentConfirmationRequests.FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Request not found.");
            if (!request.SubmittedPaymentId.HasValue || request.Status != PaymentConfirmationRequestStatus.Submitted)
                throw new InvalidOperationException("Only submitted requests can be verified.");
            if (!await LockInvoiceAsync(tenantId, request.InvoiceId, cancellationToken)) throw new KeyNotFoundException("Invoice not found.");
            var invoice = await _db.Invoices.FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == request.InvoiceId, cancellationToken)
                ?? throw new KeyNotFoundException("Invoice not found.");
            if (invoice.Status == InvoiceStatus.Cancelled)
                throw new InvalidOperationException("Cancelled invoices cannot receive payment confirmations.");
            var payment = await _db.Payments.FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == request.SubmittedPaymentId.Value && item.Status == PaymentStatus.PendingVerification, cancellationToken)
                ?? throw new InvalidOperationException("Pending payment not found.");
            var remaining = await GetRemainingAsync(tenantId, invoice, cancellationToken);
            if (payment.Amount > remaining) throw new InvalidOperationException("Payment exceeds the current remaining balance.");
            payment.Status = PaymentStatus.Verified;
            payment.VerifiedByUserId = userId;
            payment.VerifiedAt = DateTime.UtcNow;
            await SynchronizeInvoiceStatusAsync(invoice, tenantId, payment.Amount, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            verified = true;
        });
        return verified;
    }

    public async Task<bool> RejectAsync(Guid tenantId, Guid id, Guid userId, string? reason, CancellationToken cancellationToken)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        var rejected = false;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            if (!await LockRequestAsync(tenantId, id, cancellationToken)) throw new KeyNotFoundException("Request not found.");
            var request = await _db.PaymentConfirmationRequests.FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Request not found.");
            if (!request.SubmittedPaymentId.HasValue || request.Status != PaymentConfirmationRequestStatus.Submitted)
                throw new InvalidOperationException("Only submitted requests can be rejected.");
            var payment = await _db.Payments.FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == request.SubmittedPaymentId.Value && item.Status == PaymentStatus.PendingVerification, cancellationToken)
                ?? throw new InvalidOperationException("Pending payment not found.");
            payment.Status = PaymentStatus.Rejected;
            request.RejectedByUserId = userId;
            request.RejectedAt = DateTime.UtcNow;
            request.RejectionReason = Normalize(reason);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            rejected = true;
        });
        return rejected;
    }

    private async Task<decimal> GetRemainingAsync(Guid tenantId, Invoice invoice, CancellationToken cancellationToken)
    {
        var paid = await _db.Payments
            .Where(item => item.TenantId == tenantId && item.InvoiceId == invoice.Id && item.Status == PaymentStatus.Verified)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        return RoundMoney(Math.Max(0m, invoice.FinalAmountToPay - paid));
    }

    private async Task SynchronizeInvoiceStatusAsync(Invoice invoice, Guid tenantId, decimal newlyVerifiedAmount, CancellationToken cancellationToken)
    {
        if (invoice.Status == InvoiceStatus.Cancelled) return;
        var persistedVerifiedPaid = await _db.Payments
            .Where(item => item.TenantId == tenantId && item.InvoiceId == invoice.Id && item.Status == PaymentStatus.Verified)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        var paid = RoundMoney(persistedVerifiedPaid + newlyVerifiedAmount);
        var remaining = RoundMoney(invoice.FinalAmountToPay - paid);
        invoice.Status = paid == 0 ? InvoiceStatus.Pending : remaining <= 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
    }

    private async Task<Guid?> LockRequestByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT \"Id\" FROM \"PaymentConfirmationRequests\" WHERE \"TokenHash\" = @tokenHash FOR UPDATE";
        command.Parameters.AddWithValue("tokenHash", tokenHash);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    private async Task<bool> LockRequestAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT \"Id\" FROM \"PaymentConfirmationRequests\" WHERE \"Id\" = @id AND \"TenantId\" = @tenantId FOR UPDATE";
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenantId", tenantId);
        return await command.ExecuteScalarAsync(cancellationToken) != null;
    }

    private async Task<bool> LockInvoiceAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT \"Id\" FROM \"Invoices\" WHERE \"Id\" = @id AND \"TenantId\" = @tenantId FOR UPDATE";
        command.Parameters.AddWithValue("id", invoiceId);
        command.Parameters.AddWithValue("tenantId", tenantId);
        return await command.ExecuteScalarAsync(cancellationToken) != null;
    }

    private static PaymentConfirmationDetailsResponse ToDetails(PaymentConfirmationRequest request) => new(
        request.Id,
        request.InvoiceId,
        request.Invoice.InvoiceNumber,
        request.RequestedAmount,
        request.SubmittedPayment?.Amount,
        request.CurrencyCode,
        request.ExpiresAt,
        request.ExpiresAt <= DateTime.UtcNow && request.Status == PaymentConfirmationRequestStatus.Active
            ? "Expired"
            : request.Status.ToString(),
        request.SubmittedAt,
        request.RejectedAt,
        request.RejectionReason,
        request.Proof != null,
        request.SubmittedPayment?.Status.ToString());

    private static PaymentConfirmationReviewResponse ToReview(PaymentConfirmationRequest request) => new(
        request.Id,
        request.SubmittedPaymentId,
        request.InvoiceId,
        request.Invoice.InvoiceNumber,
        request.Client.FullName,
        request.Project?.Name,
        request.RequestedAmount,
        request.SubmittedPayment?.Amount,
        request.CurrencyCode,
        request.ExpiresAt,
        request.CreatedAt,
        request.ExpiresAt <= DateTime.UtcNow && request.Status == PaymentConfirmationRequestStatus.Active
            ? "Expired"
            : request.Status.ToString(),
        request.SubmittedAt,
        request.RejectedAt,
        request.RejectionReason,
        request.Proof != null,
        request.SubmittedPayment?.Status.ToString());

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
    private static string NormalizeCurrency(string? value) => string.IsNullOrWhiteSpace(value) ? "ILS" : value.Trim().ToUpperInvariant();
    private static string NormalizeLanguage(string? value)
    {
        var language = value?.Trim().ToLowerInvariant();
        if (language?.StartsWith("he", StringComparison.Ordinal) == true) return "he";
        if (language?.StartsWith("ar", StringComparison.Ordinal) == true) return "ar";
        return "en";
    }

    private static string ResolveBusinessName(Tenant tenant, Invoice invoice)
    {
        var candidates = new[] { tenant.Name, tenant.LegalBusinessName, invoice.BusinessName, invoice.LegalBusinessName };
        return candidates.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value.Trim(), "Default Tenant", StringComparison.OrdinalIgnoreCase))?.Trim()
            ?? "Clienta";
    }
}