using Microsoft.AspNetCore.Http;

namespace Clienta.Api.Services;

public sealed class UnavailableDevelopmentPaymentConfirmationProofStorage : IPaymentConfirmationProofStorage
{
    public Task<PaymentConfirmationProofUpload> UploadAsync(Guid tenantId, Guid requestId, IFormFile file, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Development proof storage is not configured; proof upload is disabled.");

    public Task<PaymentConfirmationProofReadResult?> OpenReadAsync(string storageKey, string fileName, string contentType, CancellationToken cancellationToken = default) =>
        Task.FromResult<PaymentConfirmationProofReadResult?>(null);

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
