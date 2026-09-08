using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class InterpretationReport : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    public Guid InterpretationRequestId { get; set; }
    public InterpretationRequest InterpretationRequest { get; set; } = null!;

    [Required]
    public string Content { get; set; } = string.Empty;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
