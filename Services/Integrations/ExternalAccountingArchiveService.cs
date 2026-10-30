using System.Net;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services.Integrations;

public enum AccountingArchiveOutcome
{
    Archived,
    AlreadyArchived,
    DocumentNotFound,
    DocumentNotIssued,
    CredentialUnavailable,
    CopyUnavailable,
    DownloadRejected,
    StorageFailed
}

public sealed record AccountingArchiveResult(
    AccountingArchiveOutcome Outcome,
    Guid? ClientFileId = null,
    string? ErrorCode = null);

public interface IExternalAccountingArchiveService
{
    Task<AccountingArchiveResult> ArchiveAsync(
        Guid tenantId,
        Guid externalAccountingDocumentId,
        Guid userId,
        CancellationToken ct);
}

public sealed class ExternalAccountingArchiveService
    : IExternalAccountingArchiveService
{
    private const IntegrationProvider Provider = IntegrationProvider.Rivhit;
    private const long MaxPdfBytes = ClientFileService.MaxFileSize;

    private readonly AppDbContext _db;
    private readonly ITenantIntegrationService _integrations;
    private readonly IAccountingDocumentProvider _provider;
    private readonly IClientFileService _clientFiles;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalAccountingArchiveService> _logger;

    public ExternalAccountingArchiveService(
        AppDbContext db,
        ITenantIntegrationService integrations,
        IAccountingDocumentProvider provider,
        IClientFileService clientFiles,
        HttpClient httpClient,
        ILogger<ExternalAccountingArchiveService> logger)
    {
        _db = db;
        _integrations = integrations;
        _provider = provider;
        _clientFiles = clientFiles;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<AccountingArchiveResult> ArchiveAsync(
        Guid tenantId,
        Guid externalAccountingDocumentId,
        Guid userId,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty ||
            externalAccountingDocumentId == Guid.Empty ||
            userId == Guid.Empty)
        {
            return new(AccountingArchiveOutcome.DocumentNotFound);
        }

        var document = await _db.ExternalAccountingDocuments
            .FirstOrDefaultAsync(
                x => x.Id == externalAccountingDocumentId &&
                     x.TenantId == tenantId &&
                     x.Provider == Provider,
                ct);

        if (document == null)
            return new(AccountingArchiveOutcome.DocumentNotFound);

        var existingFile = await _db.ClientFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                f => f.TenantId == tenantId &&
                     f.ExternalAccountingDocumentId == document.Id,
                ct);

        if (existingFile != null)
        {
            document.ArchiveStatus = AccountingArchiveStatus.Archived;
            document.ArchiveErrorCode = null;
            document.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new(
                AccountingArchiveOutcome.AlreadyArchived,
                existingFile.Id);
        }

        if (document.Status != ExternalAccountingDocumentStatus.Issued ||
            !document.ExternalDocumentType.HasValue ||
            !document.ExternalDocumentNumber.HasValue)
        {
            return new(AccountingArchiveOutcome.DocumentNotIssued);
        }

        document.ArchiveAttemptedAt = DateTime.UtcNow;
        document.ArchiveStatus = AccountingArchiveStatus.Pending;
        document.ArchiveErrorCode = null;

        await _db.SaveChangesAsync(ct);

        var credential =
            await _integrations.GetOperationalCredentialAsync(
                tenantId,
                Provider,
                ct);

        if (string.IsNullOrWhiteSpace(credential))
        {
            return await FailAsync(
                document,
                "CredentialUnavailable",
                AccountingArchiveOutcome.CredentialUnavailable,
                ct);
        }

        string? copyUrl;

        try
        {
            copyUrl = document.DocumentType switch
            {
                ExternalAccountingPurpose.Invoice =>
                    await _provider.GetDocumentCopyLinkAsync(
                        credential,
                        document.ExternalDocumentType.Value,
                        document.ExternalDocumentNumber.Value,
                        ct),

                ExternalAccountingPurpose.Receipt =>
                    await _provider.GetReceiptCopyLinkAsync(
                        credential,
                        document.ExternalDocumentType.Value,
                        document.ExternalDocumentNumber.Value,
                        ct),

                _ => null
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed retrieving Rivhit copy link for external accounting document {DocumentId} ({ExceptionType}).",
                document.Id,
                ex.GetType().Name);

            return await FailAsync(
                document,
                "CopyUnavailable",
                AccountingArchiveOutcome.CopyUnavailable,
                CancellationToken.None);
        }

        if (!TryValidateRivhitPdfUrl(copyUrl, out var pdfUri))
        {
            return await FailAsync(
                document,
                "InvalidCopyUrl",
                AccountingArchiveOutcome.CopyUnavailable,
                CancellationToken.None);
        }

        byte[] pdfBytes;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                pdfUri);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (IsRedirect(response.StatusCode))
            {
                return await FailAsync(
                    document,
                    "RedirectRejected",
                    AccountingArchiveOutcome.DownloadRejected,
                    CancellationToken.None);
            }

            if (!response.IsSuccessStatusCode)
            {
                return await FailAsync(
                    document,
                    "PdfDownloadFailed",
                    AccountingArchiveOutcome.DownloadRejected,
                    CancellationToken.None);
            }

            var contentType =
                response.Content.Headers.ContentType?.MediaType;

            if (!string.IsNullOrWhiteSpace(contentType) &&
                !string.Equals(
                    contentType,
                    "application/pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                return await FailAsync(
                    document,
                    "InvalidContentType",
                    AccountingArchiveOutcome.DownloadRejected,
                    CancellationToken.None);
            }

            var declaredLength =
                response.Content.Headers.ContentLength;

            if (declaredLength.HasValue &&
                (declaredLength.Value <= 0 ||
                 declaredLength.Value > MaxPdfBytes))
            {
                return await FailAsync(
                    document,
                    "PdfSizeRejected",
                    AccountingArchiveOutcome.DownloadRejected,
                    CancellationToken.None);
            }

            await using var source =
                await response.Content.ReadAsStreamAsync(ct);

            using var memory = new MemoryStream();

            var buffer = new byte[81920];
            long total = 0;

            while (true)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    ct);

                if (read == 0)
                    break;

                total += read;

                if (total > MaxPdfBytes)
                {
                    return await FailAsync(
                        document,
                        "PdfTooLarge",
                        AccountingArchiveOutcome.DownloadRejected,
                        CancellationToken.None);
                }

                await memory.WriteAsync(
                    buffer.AsMemory(0, read),
                    ct);
            }

            if (total < 5)
            {
                return await FailAsync(
                    document,
                    "InvalidPdf",
                    AccountingArchiveOutcome.DownloadRejected,
                    CancellationToken.None);
            }

            var bytes = memory.ToArray();

            if (bytes[0] != (byte)'%' ||
                bytes[1] != (byte)'P' ||
                bytes[2] != (byte)'D' ||
                bytes[3] != (byte)'F' ||
                bytes[4] != (byte)'-')
            {
                return await FailAsync(
                    document,
                    "InvalidPdf",
                    AccountingArchiveOutcome.DownloadRejected,
                    CancellationToken.None);
            }

            pdfBytes = bytes;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed downloading Rivhit PDF for external accounting document {DocumentId} ({ExceptionType}).",
                document.Id,
                ex.GetType().Name);

            return await FailAsync(
                document,
                "PdfDownloadFailed",
                AccountingArchiveOutcome.DownloadRejected,
                CancellationToken.None);
        }

        var invoice = await _db.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.Id == document.InvoiceId &&
                     i.TenantId == tenantId,
                ct);

        if (invoice == null)
        {
            return await FailAsync(
                document,
                "InvoiceNotFound",
                AccountingArchiveOutcome.StorageFailed,
                CancellationToken.None);
        }

        var title = BuildTitle(document);
        var fileName = BuildFileName(document);

        try
        {
            await using var pdfStream =
                new MemoryStream(pdfBytes, writable: false);

            var clientFile =
                await _clientFiles.CreateExternalPdfAsync(
                    tenantId,
                    invoice.ClientId,
                    invoice.ProjectId,
                    userId,
                    document.Id,
                    pdfStream,
                    pdfBytes.LongLength,
                    fileName,
                    title,
                    ct);

            document.ArchiveStatus =
                AccountingArchiveStatus.Archived;

            document.ArchiveErrorCode = null;
            document.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new(
                AccountingArchiveOutcome.Archived,
                clientFile.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Failed storing Rivhit PDF for external accounting document {DocumentId} ({ExceptionType}).",
                document.Id,
                ex.GetType().Name);

            return await FailAsync(
                document,
                "StorageFailed",
                AccountingArchiveOutcome.StorageFailed,
                CancellationToken.None);
        }
    }

    private async Task<AccountingArchiveResult> FailAsync(
        ExternalAccountingDocument document,
        string code,
        AccountingArchiveOutcome outcome,
        CancellationToken ct)
    {
        document.ArchiveStatus =
            AccountingArchiveStatus.Failed;

        document.ArchiveErrorCode = code;
        document.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return new(outcome, null, code);
    }

    private static bool TryValidateRivhitPdfUrl(
        string? value,
        out Uri uri)
    {
        uri = null!;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var candidate))
        {
            return false;
        }

        if (!string.Equals(
                candidate.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(
                candidate.Host,
                "api.rivhit.co.il",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!candidate.AbsolutePath.StartsWith(
                "/pdf/",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var value = (int)statusCode;
        return value >= 300 && value <= 399;
    }

    private static string BuildTitle(
        ExternalAccountingDocument document)
    {
        var number =
            document.ExternalDocumentNumber?.ToString()
            ?? "document";

        return document.DocumentType switch
        {
            ExternalAccountingPurpose.Invoice =>
                $"Rivhit Tax Invoice {number}",

            ExternalAccountingPurpose.Receipt =>
                $"Rivhit Receipt {number}",

            _ =>
                $"Rivhit Document {number}"
        };
    }

    private static string BuildFileName(
        ExternalAccountingDocument document)
    {
        var number =
            document.ExternalDocumentNumber?.ToString()
            ?? document.Id.ToString("N");

        return document.DocumentType switch
        {
            ExternalAccountingPurpose.Invoice =>
                $"rivhit-tax-invoice-{number}.pdf",

            ExternalAccountingPurpose.Receipt =>
                $"rivhit-receipt-{number}.pdf",

            _ =>
                $"rivhit-document-{number}.pdf"
        };
    }
}