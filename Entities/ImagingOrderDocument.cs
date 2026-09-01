using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class ImagingOrderDocument : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [Required]
    public Guid ImagingOrderId { get; set; }
    public ImagingOrder ImagingOrder { get; set; } = null!;

    [Required]
    public string DocumentType { get; set; } = ImagingOrderDocumentTypes.Referral;

    [Required]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    public string ContentType { get; set; } = "application/octet-stream";

    public long FileSize { get; set; }

    [Required]
    public byte[] FileData { get; set; } = Array.Empty<byte>();

    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }
}

public static class ImagingOrderDocumentTypes
{
    public const string Referral = "Referral";
}