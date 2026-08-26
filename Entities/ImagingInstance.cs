using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class ImagingInstance : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    public Guid ImagingStudyId { get; set; }
    public ImagingStudy ImagingStudy { get; set; } = null!;

    [Required]
    public Guid ImagingSeriesId { get; set; }
    public ImagingSeries ImagingSeries { get; set; } = null!;

    [Required]
    public string SOPInstanceUID { get; set; } = string.Empty;

    [Required]
    public string SOPClassUID { get; set; } = string.Empty;

    public int? InstanceNumber { get; set; }

    [Required]
    public string LocalFilePath { get; set; } = string.Empty;

    [Required]
    public string StorageStatus { get; set; } = ImagingStudyStorageStatuses.Local;

    public string? S3Bucket { get; set; }

    public string? S3Key { get; set; }

    public string? S3ETag { get; set; }

    public DateTime? S3UploadedAt { get; set; }

    public long? FileSizeBytes { get; set; }

    [Required]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ImagingAnnotation> Annotations { get; set; } = new List<ImagingAnnotation>();
}
