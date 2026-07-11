namespace Clienta.Api.Services.WhatsApp.Meta;

public class MetaApiException : Exception
{
    public string UserMessage { get; }
    public string AuditDetail { get; }
    public bool IsTransient { get; }

    public MetaApiException(string userMessage, string auditDetail, bool isTransient = false)
        : base(auditDetail)
    {
        UserMessage = userMessage;
        AuditDetail = auditDetail;
        IsTransient = isTransient;
    }
}
