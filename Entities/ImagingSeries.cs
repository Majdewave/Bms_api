using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class ImagingSeries : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    public Guid ImagingStudyId { get; set; }
    public ImagingStudy ImagingStudy { get; set; } = null!;

    [Required]
    public string SeriesInstanceUID { get; set; } = string.Empty;

    [Required]
    public string Modality { get; set; } = string.Empty;

    public int? SeriesNumber { get; set; }

    public string? SeriesDescription { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
