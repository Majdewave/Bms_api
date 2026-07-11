namespace Clienta.Api.Services.WhatsApp.Meta;

public class MetaWhatsAppOptions
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string EmbeddedSignupConfigId { get; set; } = string.Empty;
    public string GraphApiVersion { get; set; } = "v20.0";
    public string Scopes { get; set; } = "whatsapp_business_management,whatsapp_business_messaging,business_management";
    public string WebhookUrl { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public int StateTtlMinutes { get; set; } = 15;
    public int MaxConnectingMinutes { get; set; } = 20;
}