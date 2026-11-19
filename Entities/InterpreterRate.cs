namespace Clienta.Api.Entities;

public class InterpreterRate : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid InterpreterUserId { get; set; }
    public string Modality { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ILS";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
