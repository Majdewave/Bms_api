using System.Text.Json.Serialization;

namespace Clienta.Api.DTOs;

public class CreatePrescriptionRequest
{
    [JsonPropertyName("clientId")]
    public Guid ClientId { get; set; }

    [JsonPropertyName("staffId")]
    public Guid? StaffId { get; set; }

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

public class PrescriptionResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("clientId")]
    public Guid ClientId { get; set; }

    [JsonPropertyName("staffId")]
    public Guid? StaffId { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("drugs")]
    public List<string> Drugs { get; set; } = new();

    [JsonPropertyName("instructions")]
    public string Instructions { get; set; } = string.Empty;

    [JsonPropertyName("doctorName")]
    public string DoctorName { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("staffStampUrl")]
    public string? StaffStampUrl { get; set; }
}