namespace Clienta.Api.Entities;

public class QueueDisplayAdvertisementImage : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid QueueDisplaySettingsId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual QueueDisplaySettings QueueDisplaySettings { get; set; } = default!;
}
