using System.Text.Json.Serialization;

namespace Clienta.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProjectPaymentType
{
    Retainer = 0,
    Monthly = 1,
    Hourly = 2,
    Milestone = 3,
}