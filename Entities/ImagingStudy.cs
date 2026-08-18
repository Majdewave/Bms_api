using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class ImagingStudy : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public Guid? ImagingOrderId { get; set; }
    public ImagingOrder? ImagingOrder { get; set; }

    [Required]
    public string AccessionNumber { get; set; } = string.Empty;

    [Required]
    public string StudyInstanceUID { get; set; } = string.Empty;

    [Required]
    public string Modality { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = ImagingStudyStatuses.Received;

    [Required]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public string LocalStoragePath { get; set; } = string.Empty;

    [Required]
    public string StorageStatus { get; set; } = ImagingStudyStorageStatuses.Local;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
