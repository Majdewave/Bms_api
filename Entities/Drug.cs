namespace Clienta.Api.Entities;

public class Drug : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } // Tenant isolation
    public string Name { get; set; } = string.Empty;
    public string? Dosage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
