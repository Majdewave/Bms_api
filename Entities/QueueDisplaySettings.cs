namespace Clienta.Api.Entities;

public class QueueDisplaySettings : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string PublicToken { get; set; } = string.Empty;
    public QueueDisplayTheme Theme { get; set; } = QueueDisplayTheme.Default;
    public QueueDisplayPrivacyMode PrivacyMode { get; set; } = QueueDisplayPrivacyMode.FullName;
    public string? LogoOverrideUrl { get; set; }
    public QueueDisplayAdvertisementType AdvertisementType { get; set; } = QueueDisplayAdvertisementType.Image;
    public string? AdvertisementImageUrl { get; set; }
    public string? AdvertisementVideoUrl { get; set; }
    public string? VoiceSettingsJson { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Tenant Tenant { get; set; } = default!;
    public virtual ICollection<QueueDisplayAdvertisementImage> AdvertisementImages { get; set; } = new List<QueueDisplayAdvertisementImage>();
}
