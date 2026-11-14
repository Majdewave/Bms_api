using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class InterpretationReportTemplate : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required]
    public Guid InterpreterUserId { get; set; }
    public User InterpreterUser { get; set; } = null!;

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(16)]
    public string? Modality { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}