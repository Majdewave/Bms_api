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

    // Persistence optimization only (NOT a business field):
    // derived from StartTime.Date and used for indexing, uniqueness, and waiting-queue grouping.
    // Business logic should continue to use StartTime.
    [Required]
    public DateTime AppointmentDate { get; set; }

    [Required]
    public string Status { get; set; } = AppointmentStatuses.Scheduled;
    // Allowed: Scheduled, Waiting, InProgress, Completed, Cancelled, NoShow

    public int? QueueNumber { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid? ServiceId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Service? Service { get; set; }
    public Department? Department { get; set; }

    public Guid? StaffId { get; set; }
    public BusinessUser? Staff { get; set; }

    // Indicates if appointment is documented
    public bool IsDocumented { get; set; } = true;
}
