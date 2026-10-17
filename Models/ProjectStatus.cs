using System.Text.Json.Serialization;

namespace Clienta.Api.Models;

// JsonStringEnumConverter ensures Phase 1A/2 request+response payloads use the enum member
// names (e.g. "Draft", "OnHold") instead of raw integers, matching the frontend PM types.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProjectStatus
{
    Draft = 0,
    Active = 1,
    OnHold = 2,
    Completed = 3,
    Cancelled = 4,
}
