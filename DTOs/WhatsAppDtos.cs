namespace Clienta.Api.DTOs;

public class WhatsAppCapabilities
{
    public bool CanSendText { get; set; }
    public bool CanSendTemplate { get; set; }
    public bool CanSendDocuments { get; set; }
    public bool CanReceiveMessages { get; set; }
    public bool CanReceiveStatus { get; set; }
    public bool CanManageTemplates { get; set; }
}

public record WhatsAppStatusDto(
    string ConnectionStatus,
    bool Connected,
    string? BusinessName,
    string? PhoneNumber,
    DateTime? ConnectedSince,
    bool WebhookVerified,
    DateTime? WebhookVerifiedAt,
    string? GraphApiVersion,
    string? LastError,
    DateTime? LastErrorAt,
    int MessagesThisMonth,
    int TemplatesCount,
    int ConversationsCount,
    DateTime? LastActivity,
    WhatsAppCapabilities Capabilities
);

public record WhatsAppConnectInitResponse(
    string AppId,
    string EmbeddedSignupConfigId,
    string GraphApiVersion
);

public record WhatsAppEmbeddedSignupCompleteRequest(
    string AuthorizationCode,
    string BusinessId,
    string WabaId,
    string PhoneNumberId,
    string? DisplayPhoneNumber
);

public record WhatsAppCallbackResultDto(
    bool Success,
    string Message,
    string RedirectUrl
);

public record WhatsAppDiagnosticsCheckDto(
    string Name,
    bool Success,
    string Message,
    long DurationMs
);

public record WhatsAppConnectionDiagnosticsDto(
    bool IsHealthy,
    string ConnectionStatus,
    string? GraphApiVersion,
    string? BusinessName,
    string? BusinessAccountId,
    string? PhoneNumber,
    string? PhoneNumberId,
    bool WebhookVerified,
    bool AccessTokenValid,
    bool BusinessFound,
    bool PhoneFound,
    bool ConfigurationValid,
    DateTime? LastWebhookReceivedAt,
    string? LastError,
    IReadOnlyList<WhatsAppDiagnosticsCheckDto> Checks
);
