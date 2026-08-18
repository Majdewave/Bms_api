using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class ImagingGatewayCredential : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    [MaxLength(32)]
    public string KeyId { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string KeyHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }
}
