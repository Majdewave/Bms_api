namespace Clienta.Api.Entities;

public class ConsentTemplate : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public Guid ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
