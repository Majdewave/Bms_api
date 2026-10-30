using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public enum IntegrationProvider
{
    Rivhit = 1
}

public enum IntegrationConnectionStatus
{
    Unknown = 0,
    Connected = 1,
    Failed = 2
}

// Tenant-scoped external integration configuration. One row per (tenant, provider).
// Disabling or disconnecting never deletes the row, so later mappings/documents keep their anchor.
public class TenantIntegration : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public IntegrationProvider Provider { get; set; }

    public bool IsEnabled { get; set; }
    public bool AccountingEnabled { get; set; } = true;

    // Reserved for a later phase; never honoured in Phase 1.
    public bool PaymentsEnabled { get; set; }

    // Protected by IIntegrationSecretProtector. Never exposed.
    [MaxLength(2000)]
    public string? EncryptedCredential { get; set; }

    public IntegrationConnectionStatus ConnectionStatus { get; set; } = IntegrationConnectionStatus.Unknown;
    public DateTime? LastConnectionTestAt { get; set; }
    public DateTime? LastConnectionSucceededAt { get; set; }

    // Sanitized provider-neutral failure code, never raw provider output.
    [MaxLength(50)]
    public string? LastConnectionErrorCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
