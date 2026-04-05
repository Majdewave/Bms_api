using System.Text.Json.Serialization;

namespace Clienta.Api.DTOs;

public class ConsentTemplateRequest
{
    [JsonPropertyName("serviceId")]
    public Guid ServiceId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class SignConsentRequest
{
    [JsonPropertyName("templateId")]
    public Guid TemplateId { get; set; }

    [JsonPropertyName("clientId")]
    public Guid ClientId { get; set; }

    [JsonPropertyName("appointmentId")]
    public Guid? AppointmentId { get; set; }

    [JsonPropertyName("serviceId")]
    public Guid? ServiceId { get; set; }

    [JsonPropertyName("consentContent")]
    public string ConsentContent { get; set; } = string.Empty;

    [JsonPropertyName("clientSignatureBase64")]
    public string? ClientSignatureBase64 { get; set; }
}

public class ConsentResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("clientId")]
    public Guid ClientId { get; set; }

    [JsonPropertyName("appointmentId")]
    public Guid? AppointmentId { get; set; }

    [JsonPropertyName("consentContent")]
    public string ConsentContent { get; set; } = string.Empty;

    [JsonPropertyName("clientSignatureUrl")]
    public string? ClientSignatureUrl { get; set; }

    [JsonPropertyName("signedAt")]
    public DateTime SignedAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
