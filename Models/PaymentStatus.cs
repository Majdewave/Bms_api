using System.Text.Json.Serialization;

namespace Clienta.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentStatus
{
    PendingVerification = 0,
    Verified = 1,
    Rejected = 2,
    Cancelled = 3,
}
