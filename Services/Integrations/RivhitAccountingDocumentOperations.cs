using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clienta.Api.Entities;

namespace Clienta.Api.Services.Integrations;

public sealed class RivhitProviderException : Exception
{
    public string SafeCode { get; }

    public RivhitProviderException(string safeCode) : base(safeCode) => SafeCode = safeCode;
}

public partial class RivhitAccountingProvider : IAccountingDocumentProvider, IAccountingMetadataProvider
{
    private const int UnpaidTaxInvoiceType = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IReadOnlyList<ProviderPaymentType>> GetPaymentTypesAsync(string credential, CancellationToken cancellationToken)
    {
        var root = await ReadEnvelopeAsync<RivhitPaymentTypeListData>("Payment.TypeList", new { api_token = credential }, cancellationToken);
        if (root.ErrorCode != 0 || root.Data?.PaymentTypes is null)
            throw new RivhitProviderException("PaymentTypesUnavailable");

        return root.Data.PaymentTypes
            .Where(item => item.PaymentType.HasValue)
            .Select(item => new ProviderPaymentType(item.PaymentType!.Value, SanitizeName(item.PaymentName), item.TypeCode))
            .ToArray();
    }

    public async Task<IReadOnlyList<ProviderReceiptType>> GetReceiptTypesAsync(string credential, CancellationToken cancellationToken)
    {
        var root = await ReadEnvelopeAsync<RivhitReceiptTypeListData>("Receipt.TypeList", new { api_token = credential }, cancellationToken);
        if (root.ErrorCode != 0 || root.Data?.ReceiptTypes is null)
            throw new RivhitProviderException("ReceiptTypesUnavailable");

        return root.Data.ReceiptTypes
            .Where(item => item.ReceiptType.HasValue)
            .Select(item => new ProviderReceiptType(
                item.ReceiptType!.Value,
                SanitizeName(item.ReceiptName),
                item.IsInvoiceReceipt,
                item.PaymentType,
                item.StartNumber,
                item.OpenFormat,
                item.Clause46))
            .ToArray();
    }

    private static string? SanitizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = new string(value.Where(character => !char.IsControl(character)).Take(120).ToArray()).Trim();
        return safe.Length == 0 ? null : safe;
    }

    public async Task<IReadOnlyList<ProviderDocumentType>> GetDocumentTypesAsync(string credential, CancellationToken cancellationToken)
    {
        var root = await ReadEnvelopeAsync<RivhitDocumentTypeData>("Document.TypeList", new { api_token = credential }, cancellationToken);
        if (root.ErrorCode != 0 || root.Data?.DocumentTypes is null)
            throw new RivhitProviderException("ProviderMetadataUnavailable");

        return root.Data.DocumentTypes
            .Where(x => x.DocumentType.HasValue)
                .Select(x => new ProviderDocumentType(x.DocumentType!.Value, x.Name, x.IsInvoiceReceipt, x.IsAccounting, x.PriceIncludesVat))
            .ToArray();
    }

    public async Task<decimal> GetVatRateAsync(string credential, CancellationToken cancellationToken)
    {
        var root = await ReadEnvelopeAsync<RivhitVatRateData>("Accounting.VatRate", new { api_token = credential }, cancellationToken);
        if (root.ErrorCode != 0 || root.Data?.VatRate is not decimal rate || rate < 0 || rate > 100)
            throw new RivhitProviderException("VatRateUnavailable");
        return rate;
    }

    public async Task<ProviderMutationResult> IssueInvoiceAsync(string credential, AccountingInvoiceIssueRequest request, CancellationToken cancellationToken)
    {
        var body = new RivhitDocumentNewRequest
        {
            ApiToken = credential,
            DocumentType = UnpaidTaxInvoiceType,
            CustomerId = request.ExistingCustomerId ?? 0,
            LastName = request.ClientName,
            Address = request.ClientAddress,
            Phone = request.ClientPhone,
            IdNumber = request.ExistingCustomerId.HasValue ? null : request.ClientIdNumber,
            CurrencyId = 1,
            ExchangeRate = 1m,
            IssueDate = request.IssueDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            DueDate = request.DueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            PriceIncludesVat = false,
            RoundDigits = 2,
            Items = request.Lines.Select(line => new RivhitDocumentItem
            {
                Description = line.Description,
                Quantity = line.Quantity,
                PriceNis = line.UnitPrice,
                ExemptVat = false
            }).ToArray(),
            CreateCustomer = !request.ExistingCustomerId.HasValue,
            FindById = !request.ExistingCustomerId.HasValue && request.ClientIdNumber.HasValue,
            RequestReference = request.RequestReference,
            PreventDuplicates = true,
            SendMail = request.SendEmail,
            EmailTo = request.SendEmail ? request.EmailTo : null
        };

        try
        {
           using var response = await _http.PostAsJsonAsync(
    "Document.New",
    body,
    JsonOptions,
    cancellationToken);

        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if ((int)response.StatusCode >= 500 ||
            response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Rivhit Document.New returned HTTP {HttpStatus}.", (int)response.StatusCode);

            // Rivhit can return a deterministic business rejection with a 5xx status.
            // If the body parses and carries a non-zero error_code, treat it as Rejected
            // (do NOT re-issue; never auto-retry Document.New). Empty/unparseable/ambiguous
            // bodies stay Unknown so the caller flags ReconciliationRequired.
            if (TryParseDeterministicError(text, out var safeCode))
                return new(ProviderMutationDisposition.Rejected,
                    new ProviderInvoiceDocument(UnpaidTaxInvoiceType, null, null, null, null, safeCode));

            return new(ProviderMutationDisposition.Unknown, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Rivhit Document.New was rejected with HTTP {HttpStatus}.", (int)response.StatusCode);

            return new(ProviderMutationDisposition.Rejected, null);
        }
            if (!TryParseDocumentEnvelope(text, out var errorCode, out var document))
                return new(ProviderMutationDisposition.Unknown, null);
            if (errorCode != 0)
                return new(ProviderMutationDisposition.Rejected, document);
            if (document is null || document.DocumentType != UnpaidTaxInvoiceType || !document.DocumentNumber.HasValue || string.IsNullOrWhiteSpace(document.DocumentIdentity))
                return new(ProviderMutationDisposition.Unknown, null);

            return new(ProviderMutationDisposition.Success, document);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(ProviderMutationDisposition.Unknown, null);
        }
        catch (HttpRequestException)
        {
            return new(ProviderMutationDisposition.Unknown, null);
        }
        catch (JsonException)
        {
            return new(ProviderMutationDisposition.Unknown, null);
        }
    }

    public async Task<ProviderReceiptMutationResult> IssueReceiptAsync(string credential, AccountingReceiptIssueRequest request, CancellationToken cancellationToken)
    {
        var body = new RivhitReceiptNewRequest
        {
            ApiToken = credential,
            ReceiptType = request.ReceiptType,
            CustomerId = request.CustomerId,
            LastName = request.CustomerName,
            Address = request.CustomerAddress,
            Phone = request.CustomerPhone,
            IdNumber = request.CreateCustomer ? request.CustomerIdNumber : null,
            IssueDate = request.PaymentDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            CurrencyId = 1,
            ExchangeRate = 1m,
            Payments = new[] { new RivhitReceiptPaymentRow { PaymentType = request.PaymentType, AmountNis = request.Amount } },
            RequestReference = request.RequestReference,
            PreventDuplicates = true,
            CreateCustomer = request.CreateCustomer,
            FindById = request.FindById,
            SendMail = request.SendEmail,
            EmailTo = request.SendEmail ? request.EmailTo : null
        };

        try
        {
            using var response = await _http.PostAsJsonAsync("Receipt.New", body, JsonOptions, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Rivhit Receipt.New returned HTTP {HttpStatus}.", (int)response.StatusCode);

                // Deterministic Rivhit business rejection over 5xx => Rejected (no retry of Receipt.New).
                // Empty/unparseable/ambiguous bodies stay Unknown.
                if (TryParseDeterministicError(text, out var safeCode))
                    return new(ProviderMutationDisposition.Rejected,
                        new ProviderReceiptDocument(null, null, null, null, null, null, safeCode));

                return new(ProviderMutationDisposition.Unknown, null);
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Rivhit Receipt.New was rejected with HTTP {HttpStatus}.", (int)response.StatusCode);
                return new(ProviderMutationDisposition.Rejected, new ProviderReceiptDocument(null, null, null, null, null, null, $"Http{(int)response.StatusCode}"));
            }

            if (!TryParseReceiptEnvelope(text, out var errorCode, out var document))
                return new(ProviderMutationDisposition.Unknown, null);
            if (errorCode != 0)
                return new(ProviderMutationDisposition.Rejected, document ?? new ProviderReceiptDocument(null, null, null, null, null, null, $"Rivhit{errorCode}"));
            if (document is null || document.DocumentType != request.ReceiptType || !document.DocumentNumber.HasValue
                || string.IsNullOrWhiteSpace(document.DocumentIdentity) || !document.Amount.HasValue)
                return new(ProviderMutationDisposition.Unknown, document);

            return new(ProviderMutationDisposition.Success, document);
        }
        catch (OperationCanceledException)
        {
            // Cancellation after transmission is ambiguous; never retry Receipt.New automatically.
            return new(ProviderMutationDisposition.Unknown, null);
        }
        catch (HttpRequestException)
        {
            return new(ProviderMutationDisposition.Unknown, null);
        }
        catch (JsonException)
        {
            return new(ProviderMutationDisposition.Unknown, null);
        }
    }

    public async Task<ProviderDocumentDetails?> GetDocumentDetailsAsync(string credential, int documentType, int documentNumber, CancellationToken cancellationToken)
    {
        try
        {
            var root = await ReadEnvelopeAsync<RivhitDocumentDetailsData>("Document.Details", new
            {
                api_token = credential,
                document_type = documentType,
                document_number = documentNumber
            }, cancellationToken);
            if (root.ErrorCode != 0 || root.Data is null) return null;
            return new ProviderDocumentDetails(root.Data.Total, root.Data.TotalWithoutVat, root.Data.TotalVat,
                root.Data.IssueDate, root.Data.DueDate, root.Data.CurrencyId, root.Data.PriceIncludesVat);
        }
        catch (RivhitProviderException)
        {
            return null;
        }
    }

    public async Task<ProviderRequestRecovery> GetLastRequestAsync(string credential, string requestReference, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("Status.LastRequest/JSON", new { api_token = credential, request_reference = requestReference }, JsonOptions, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent) return new(ProviderRequestStatus.NotFound, null);
            if (!response.IsSuccessStatusCode) return new(ProviderRequestStatus.Unknown, null);

            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

            using var json = JsonDocument.Parse(rawResponse);
            if (!json.RootElement.TryGetProperty("error_code", out var code) || !code.TryGetInt32(out var errorCode))
                return new(ProviderRequestStatus.Unknown, null);
            if (errorCode != 0)
                return new(errorCode == 204 ? ProviderRequestStatus.NotFound : ProviderRequestStatus.Unknown, null);
               
                if (!json.RootElement.TryGetProperty("data", out var data))
                    return new(ProviderRequestStatus.Unknown, null);

                return ParseRecoveredResponse(
                    data.ValueKind == JsonValueKind.String
                        ? data.GetString()
                        : data.GetRawText());

        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(ProviderRequestStatus.Unknown, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new(ProviderRequestStatus.Unknown, null);
        }
    }

    public async Task<IReadOnlyList<ProviderRequestRecovery>> GetAllRequestsAsync(string credential, string requestReference, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("Status.AllRequests/JSON", new { api_token = credential, request_reference = requestReference }, JsonOptions, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent) return Array.Empty<ProviderRequestRecovery>();
            if (!response.IsSuccessStatusCode) return new[] { new ProviderRequestRecovery(ProviderRequestStatus.Unknown, null) };

            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

            using var json = JsonDocument.Parse(rawResponse);

            if (!json.RootElement.TryGetProperty("error_code", out var code) || !code.TryGetInt32(out var errorCode))
                return new[] { new ProviderRequestRecovery(ProviderRequestStatus.Unknown, null) };
            if (errorCode != 0)
                return errorCode == 204 ? Array.Empty<ProviderRequestRecovery>() : new[] { new ProviderRequestRecovery(ProviderRequestStatus.Unknown, null) };
            if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new[] { new ProviderRequestRecovery(ProviderRequestStatus.Unknown, null) };

            return data.EnumerateArray()
                .Select(item => ParseRecoveredResponse(item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText()))
                .ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new[] { new ProviderRequestRecovery(ProviderRequestStatus.Unknown, null) };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new[] { new ProviderRequestRecovery(ProviderRequestStatus.Unknown, null) };
        }
    }

    private async Task<RivhitEnvelope<T>> ReadEnvelopeAsync<T>(string operation, object body, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(operation, body, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Rivhit metadata operation {Operation} returned HTTP {HttpStatus}.", operation, (int)response.StatusCode);
                throw new RivhitProviderException("ProviderUnavailable");
            }
            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            if (_hostEnvironment?.IsDevelopment() == true && operation == "Accounting.VatRate")
            {
                LogVatRateResponse(response.StatusCode, rawResponse);
            }

            var envelope = JsonSerializer.Deserialize<RivhitEnvelope<T>>(rawResponse, JsonOptions);
            return envelope ?? throw new RivhitProviderException("InvalidProviderResponse");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RivhitProviderException("ProviderTimeout");
        }
        catch (HttpRequestException)
        {
            throw new RivhitProviderException("ProviderUnavailable");
        }
        catch (JsonException)
        {
            throw new RivhitProviderException("InvalidProviderResponse");
        }
    }

    private void LogVatRateResponse(HttpStatusCode statusCode, string responseJson)
    {
        var parsedErrorCode = "Unavailable";
        var parsedVatRate = "Unavailable";
        try
        {
            using var json = JsonDocument.Parse(responseJson);
            if (json.RootElement.TryGetProperty("error_code", out var errorCode))
                parsedErrorCode = errorCode.GetRawText();
            if (json.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("vat_rate", out var vatRate))
                parsedVatRate = vatRate.GetRawText();
        }
        catch (JsonException)
        {
            // Preserve the existing provider parsing outcome; diagnostics report unavailable fields.
        }

        _logger.LogInformation(
            "[RIVHIT DEV Accounting.VatRate] HttpStatus={HttpStatus} ParsedErrorCode={ErrorCode} ParsedVatRate={VatRate}",
            (int)statusCode,
            parsedErrorCode,
            parsedVatRate);
    }

    public async Task<ProviderDeliveryResult> SendDocumentByEmailAsync(string credential, int documentType, int documentNumber, string email, CancellationToken cancellationToken)
        => await SendByEmailAsync("Document.Send", new { api_token = credential, document_type = documentType, document_number = documentNumber, email }, cancellationToken);

    public async Task<ProviderDeliveryResult> SendReceiptByEmailAsync(string credential, int receiptType, int receiptNumber, string email, CancellationToken cancellationToken)
        => await SendByEmailAsync("Receipt.Send", new { api_token = credential, receipt_type = receiptType, receipt_number = receiptNumber, email }, cancellationToken);

    public async Task<string?> GetDocumentCopyLinkAsync(string credential, int documentType, int documentNumber, CancellationToken cancellationToken)
        => await GetCopyLinkAsync("Document.Copy", new { api_token = credential, document_type = documentType, document_number = documentNumber }, cancellationToken);

    public async Task<string?> GetReceiptCopyLinkAsync(string credential, int receiptType, int receiptNumber, CancellationToken cancellationToken)
        => await GetCopyLinkAsync("Receipt.Copy", new { api_token = credential, receipt_type = receiptType, receipt_number = receiptNumber }, cancellationToken);

    private async Task<ProviderDeliveryResult> SendByEmailAsync(string operation, object body, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(operation, body, JsonOptions, cancellationToken);
            if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Rivhit delivery operation {Operation} returned HTTP {HttpStatus}.", operation, (int)response.StatusCode);
                return new(ProviderMutationDisposition.Unknown, "ProviderUnavailable");
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Rivhit delivery operation {Operation} was rejected with HTTP {HttpStatus}.", operation, (int)response.StatusCode);
                return new(ProviderMutationDisposition.Rejected, $"Http{(int)response.StatusCode}");
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!json.RootElement.TryGetProperty("error_code", out var code) || !code.TryGetInt32(out var errorCode))
                return new(ProviderMutationDisposition.Unknown, "InvalidProviderResponse");
            return errorCode == 0
                ? new(ProviderMutationDisposition.Success, null)
                : new(ProviderMutationDisposition.Rejected, $"Rivhit{errorCode}");
        }
        catch (OperationCanceledException)
        {
            return new(ProviderMutationDisposition.Unknown, "ProviderTimeout");
        }
        catch (HttpRequestException)
        {
            return new(ProviderMutationDisposition.Unknown, "ProviderUnavailable");
        }
        catch (JsonException)
        {
            return new(ProviderMutationDisposition.Unknown, "InvalidProviderResponse");
        }
    }

    private async Task<string?> GetCopyLinkAsync(string operation, object body, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(operation, body, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Rivhit copy operation {Operation} returned HTTP {HttpStatus}.", operation, (int)response.StatusCode);
                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!json.RootElement.TryGetProperty("error_code", out var code) || !code.TryGetInt32(out var errorCode) || errorCode != 0)
                return null;
            if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return null;
            if (!data.TryGetProperty("document_link", out var link) || link.ValueKind != JsonValueKind.String)
                return null;
            var value = link.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    private static ProviderRequestRecovery ParseRecoveredResponse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new(ProviderRequestStatus.Unknown, null);
        if (!TryParseDocumentEnvelope(content, out var code, out var document)) return new(ProviderRequestStatus.Unknown, null);
        if (code != 0) return new(ProviderRequestStatus.Rejected, document);
        if (document is null || document.DocumentType != UnpaidTaxInvoiceType || !document.DocumentNumber.HasValue || string.IsNullOrWhiteSpace(document.DocumentIdentity))
            return new(ProviderRequestStatus.Unknown, null);
        return new(ProviderRequestStatus.Success, document);
    }

    /// <summary>
    /// Extracts a deterministic Rivhit business rejection from an error body. Returns true only when the
    /// body parses as JSON and carries a non-zero numeric error_code (e.g. -28 INVALID_ID_NUMBER).
    /// Empty, non-JSON, or zero/ambiguous bodies return false so callers keep the ambiguous path.
    /// </summary>
    private static bool TryParseDeterministicError(string? content, out string safeCode)
    {
        safeCode = "ProviderRejected";
        if (string.IsNullOrWhiteSpace(content)) return false;
        try
        {
            using var json = JsonDocument.Parse(content);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("error_code", out var code) || !code.TryGetInt32(out var errorCode)) return false;
            if (errorCode == 0) return false; // success marker, not a rejection
            safeCode = SanitizeErrorCode($"Rivhit{errorCode}", root);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string SanitizeErrorCode(string fallback, JsonElement root)
    {
        // Prefer a short debug/client message when present and safe; fall back to the numeric code.
        var message = root.TryGetProperty("debug_message", out var dbg) && dbg.ValueKind == JsonValueKind.String ? dbg.GetString()
            : root.TryGetProperty("client_message", out var cm) && cm.ValueKind == JsonValueKind.String ? cm.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(message))
        {
            var safe = new string(message.Trim().Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').Take(50).ToArray());
            if (safe.Length > 0) return $"Rivhit_{safe}";
        }
        return fallback;
    }

    private static bool TryParseDocumentEnvelope(string content, out int errorCode, out ProviderInvoiceDocument? document)
    {
        errorCode = -1;
        document = null;
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error_code", out var code) || !code.TryGetInt32(out errorCode)) return false;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            int? Int(string name) => data.TryGetProperty(name, out var p) && p.TryGetInt32(out var v) ? v : null;
            decimal? Decimal(string name) => data.TryGetProperty(name, out var p) && p.TryGetDecimal(out var v) ? v : null;
            string? String(string name) => data.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            document = new ProviderInvoiceDocument(Int("document_type") ?? UnpaidTaxInvoiceType, Int("document_number"), String("document_identity"), Int("customer_id"), Decimal("amount"), code.ToString());
        }
        return true;
    }

    private static bool TryParseReceiptEnvelope(string content, out int errorCode, out ProviderReceiptDocument? document)
    {
        errorCode = -1;
        document = null;
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error_code", out var code) || !code.TryGetInt32(out errorCode)) return false;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            int? Int(string name) => data.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : null;
            decimal? Decimal(string name) => data.TryGetProperty(name, out var property) && property.TryGetDecimal(out var value) ? value : null;
            string? String(string name) => data.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
            document = new ProviderReceiptDocument(Int("document_type"), Int("document_number"), Int("customer_id"),
                String("document_identity"), Decimal("amount"), Int("confirmation_number"), errorCode.ToString(CultureInfo.InvariantCulture));
        }
        return true;
    }
}
