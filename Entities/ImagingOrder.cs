using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class ImagingOrder : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public Guid? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }

    public Guid? ServiceId { get; set; }
    public Service? Service { get; set; }

    [Required]
    public string AccessionNumber { get; set; } = string.Empty;

    [Required]
    public string Modality { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = ImagingOrderStatuses.Scheduled;

    public string? ReferringDoctorName { get; set; }

    [Required]
    public DateTime ScheduledStartTime { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<ImagingOrderDocument> Documents { get; set; } = new List<ImagingOrderDocument>();
}
