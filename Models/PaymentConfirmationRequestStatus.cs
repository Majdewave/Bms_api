using System.Text.Json.Serialization;

namespace Clienta.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentConfirmationRequestStatus
{
    Active = 0,
    Submitted = 1,
    Revoked = 2,
}