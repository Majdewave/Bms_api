namespace Clienta.Api.Entities;

public class InterpretationReportDocument : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid InterpretationRequestId { get; set; }
    public InterpretationRequest InterpretationRequest { get; set; } = null!;
    public Guid InterpretationReportId { get; set; }
    public InterpretationReport InterpretationReport { get; set; } = null!;
    public int Version { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/pdf";
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }
}