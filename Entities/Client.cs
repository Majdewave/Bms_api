using System.ComponentModel.DataAnnotations.Schema;

namespace Clienta.Api.Entities;

public class Client : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public virtual Tenant Tenant { get; set; } = default!;
    public string FullName { get; set; } = string.Empty;
    public string? IdNumber { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? InternalNote { get; set; }
    public bool IsDocumented { get; set; } = true;
    public bool IsNotDocumented { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public string Status
    {
        get => IsActive ? "active" : "inactive";
        set => IsActive = string.Equals(value, "active", StringComparison.OrdinalIgnoreCase);
    }

    public virtual ICollection<Note> Notes { get; set; } = new List<Note>();
}
