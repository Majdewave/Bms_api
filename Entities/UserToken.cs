namespace Clienta.Api.Entities;

public class UserToken : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = default!;

    public string Type { get; set; } = default!; // Invite / ResetPassword

    public DateTime ExpiryDate { get; set; }

    public bool IsUsed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual Tenant Tenant { get; set; } = default!;
    public virtual User User { get; set; } = default!;
}
