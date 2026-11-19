namespace Clienta.Api.Entities;

public class InterpreterWorkItem : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid InterpretationRequestId { get; set; }
    public Guid ImagingOrderId { get; set; }
    public Guid ImagingStudyId { get; set; }
    public Guid InterpreterUserId { get; set; }
    public DateTime CompletedAt { get; set; }
    public string OriginalModality { get; set; } = string.Empty;
    public string? PayrollModality { get; set; }
    public string AccessionNumber { get; set; } = string.Empty;
    public decimal? RateAmount { get; set; }
    public string? Currency { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
