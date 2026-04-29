using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class ClientFile : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public virtual Tenant Tenant { get; set; } = default!;

    [Required]
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [Required]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string StoredFileName { get; set; } = string.Empty;

    [Required]
    public string FilePath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
