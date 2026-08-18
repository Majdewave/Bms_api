namespace Clienta.Api.Entities;

public static class ImagingOrderStatuses
{
    public const string Scheduled = "Scheduled";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All =
    {
        Scheduled, InProgress, Completed, Cancelled
    };
}
