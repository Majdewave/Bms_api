namespace Clienta.Api.Entities;

public static class AppointmentStatuses
{
    public const string Scheduled = "Scheduled";
    public const string Waiting = "Waiting";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";

    public static readonly string[] All =
    {
        Scheduled, Waiting, InProgress, Completed, Cancelled, NoShow
    };
}