namespace Clienta.Api.Entities;

// PM (Project Management) foundation entity - Phase 1A. Additive only; does not replace Client.Email/Client.Phone.
public class ClientContact : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    public virtual Client Client { get; set; } = default!;
    public string Name { get; set; } = string.Empty;
    public string? RoleTitle { get; set; }
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? Email { get; set; }
    public bool IsPrimary { get; set; } = false;
    public bool IsBillingContact { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
