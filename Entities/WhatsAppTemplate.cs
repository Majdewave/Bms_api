using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class WhatsAppTemplate : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Language { get; set; } = "he";

    [MaxLength(120)]
    public string MetaTemplateName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
