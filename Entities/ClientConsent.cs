namespace Clienta.Api.Entities;

public class ClientConsent : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public Guid AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;

    public string ConsentContent { get; set; } = string.Empty;
    public string? ClientSignatureUrl { get; set; }

    public DateTime SignedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
