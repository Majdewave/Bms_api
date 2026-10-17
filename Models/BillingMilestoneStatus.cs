using System.Text.Json.Serialization;

namespace Clienta.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BillingMilestoneStatus
{
    Planned = 0,
    ReadyToInvoice = 1,
    Invoiced = 2,
    Cancelled = 3,
}
