namespace Clienta.Api.Entities;

public class AuditLog : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? UserId { get; set; }

    public string EntityName { get; set; } = string.Empty;

    public string ActionType { get; set; } = string.Empty;
    // Create / Update / Delete

    public string EntityId { get; set; } = string.Empty;

    public string? OldValues { get; set; }
    public string? NewValues { get; set; }

    public string? PerformedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual User? User { get; set; } = null;
    public virtual Tenant Tenant { get; set; } = default!;
}
