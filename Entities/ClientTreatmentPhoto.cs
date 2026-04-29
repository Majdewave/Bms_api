namespace Clienta.Api.Entities;

public class ClientTreatmentPhoto : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public string? BeforeImageUrl { get; set; }
    public string? AfterImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
