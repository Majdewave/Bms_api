using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class WhatsAppMessage : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? AppointmentId { get; set; }

    public WhatsAppMessageDirection Direction { get; set; } = WhatsAppMessageDirection.Outgoing;
    public WhatsAppMessageStatus Status { get; set; } = WhatsAppMessageStatus.Pending;
    public WhatsAppMessageType Type { get; set; } = WhatsAppMessageType.Text;

    [MaxLength(40)]
    public string PhoneNumber { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? TemplateName { get; set; }

    public string? MessageText { get; set; }

    [MaxLength(120)]
    public string? MetaMessageId { get; set; }

    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string? FailedReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum WhatsAppMessageDirection
{
    Incoming = 1,
    Outgoing = 2,
}

public enum WhatsAppMessageStatus
{
    Pending = 1,
    Queued = 2,
    Sending = 3,
    Sent = 4,
    Delivered = 5,
    Read = 6,
    Failed = 7,
}

public enum WhatsAppMessageType
{
    Text = 1,
    Template = 2,
    Document = 3,
    Image = 4,
    Location = 5,
}
