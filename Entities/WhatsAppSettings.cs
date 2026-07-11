using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class WhatsAppSettings : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    [MaxLength(200)]
    public string? BusinessName { get; set; }

    [MaxLength(50)]
    public string? DisplayPhoneNumber { get; set; }

    [MaxLength(100)]
    public string? PhoneNumberId { get; set; }

    [MaxLength(100)]
    public string? BusinessAccountId { get; set; }

    [MaxLength(100)]
    public string? BusinessId { get; set; }

    // Encrypted via EF value converter in AppDbContext.
    public string? AccessToken { get; set; }

    [MaxLength(200)]
    public string? VerifyToken { get; set; }

    [MaxLength(200)]
    public string? WebhookSecret { get; set; }

    public bool WebhookVerified { get; set; } = false;

    [MaxLength(300)]
    public string? WebhookUrl { get; set; }
    public DateTime? WebhookVerifiedAt { get; set; }
    public DateTime? LastWebhookReceivedAt { get; set; }

    [MaxLength(20)]
    public string? GraphApiVersion { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }
    public DateTime? LastErrorAt { get; set; }

    public DateTime? TokenExpiresAt { get; set; }
    public DateTime? LastSyncAt { get; set; }

    public WhatsAppConnectionStatus ConnectionStatus { get; set; } = WhatsAppConnectionStatus.Disconnected;

    public bool IsConnected { get; set; } = false;
    public DateTime? ConnectedSince { get; set; }
    public DateTime? LastActivity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum WhatsAppConnectionStatus
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Error = 3,
    TokenExpired = 4,
}
