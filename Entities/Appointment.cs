using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class Appointment : ITenantEntity
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
    public DateTime StartTime { get; set; }

    [Required]
    public DateTime EndTime { get; set; }

    [Required]
    public string Status { get; set; } = AppointmentStatuses.Scheduled;
    // Allowed: Scheduled, Waiting, InProgress, Completed, Cancelled, NoShow

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid? ServiceId { get; set; }
    public Service? Service { get; set; }

    public Guid? StaffId { get; set; }
    public BusinessUser? Staff { get; set; }

    // Indicates if appointment is documented
    public bool IsDocumented { get; set; } = true;
}
