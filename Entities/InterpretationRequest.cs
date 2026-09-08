using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class InterpretationRequest : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    public Guid ImagingOrderId { get; set; }
    public ImagingOrder ImagingOrder { get; set; } = null!;

    [Required]
    public Guid ImagingStudyId { get; set; }
    public ImagingStudy ImagingStudy { get; set; } = null!;

    [Required]
    public Guid AssignedInterpreterId { get; set; }
    public User AssignedInterpreter { get; set; } = null!;

    [Required]
    public string Status { get; set; } = InterpretationRequestStatuses.Pending;

    [Required]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public Guid RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; } = null!;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public static class InterpretationRequestStatuses
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
}