using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class Note : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public virtual Tenant Tenant { get; set; } = default!;

    [Required]
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [Required]
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
