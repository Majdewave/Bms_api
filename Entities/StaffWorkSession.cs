namespace Clienta.Api.Entities;

public class StaffWorkSession : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ClockInAt { get; set; }
    public DateTime? ClockOutAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? ClockOutByUserId { get; set; }
    public Guid? LastCorrectedByUserId { get; set; }
    public DateTime? LastCorrectedAt { get; set; }
    public string? CorrectionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
