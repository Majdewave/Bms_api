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
    [MaxLength(20)]
    public string? StorageProvider { get; set; }

    [MaxLength(2048)]
    public string? StorageKey { get; set; }

    public long FileSize { get; set; }

    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // PM Phase 7: null = general client file.
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    // Rivhit Phase 3E.2: set when this file is the archived official copy of a provider document.
    public Guid? ExternalAccountingDocumentId { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }
}
