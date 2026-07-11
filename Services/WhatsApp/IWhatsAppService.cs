using Clienta.Api.DTOs;
using Clienta.Api.Entities;

namespace Clienta.Api.Services.WhatsApp;

public interface IWhatsAppService
{
    Task<WhatsAppStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<WhatsAppConnectionDiagnosticsDto> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<WhatsAppConnectInitResponse> InitializeConnectAsync(CancellationToken cancellationToken = default);
    Task CompleteEmbeddedSignupAsync(WhatsAppEmbeddedSignupCompleteRequest request, CancellationToken cancellationToken = default);
    Task<WhatsAppCallbackResultDto> HandleCallbackAsync(string? code, string? state, string? error, string? errorDescription, string? callbackHost, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WhatsAppTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WhatsAppMessage>> GetMessagesAsync(CancellationToken cancellationToken = default);
}
