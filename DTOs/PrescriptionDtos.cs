using System.Text.Json.Serialization;

namespace Clienta.Api.DTOs;

public class CreatePrescriptionRequest
{
    [JsonPropertyName("clientId")]
    public Guid ClientId { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("drugs")]
    public List<string> Drugs { get; set; } = new();

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("doctorName")]
    public string? DoctorName { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}