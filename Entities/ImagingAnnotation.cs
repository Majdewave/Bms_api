using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class ImagingAnnotation : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    public Guid ImagingInstanceId { get; set; }
    public ImagingInstance ImagingInstance { get; set; } = null!;

    [Required]
    public int FrameNumber { get; set; }

    [Required]
    public string AnnotationUid { get; set; } = string.Empty;

    [Required]
    public string ToolName { get; set; } = string.Empty;

    [Required]
    public string Geometry { get; set; } = string.Empty;

    public string? Label { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [Required]
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}
