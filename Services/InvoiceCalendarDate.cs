namespace Clienta.Api.Services;

/// <summary>
/// Keeps invoice issue/due dates as calendar dates while the existing database columns remain timestamptz.
/// New values use UTC midnight as a stable storage representation, not as a timezone conversion.
/// </summary>
public static class InvoiceCalendarDate
{
    public static DateTime ToStorageDateTime(DateOnly value) =>
        DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    public static DateOnly FromStorageDateTime(DateTime value) => DateOnly.FromDateTime(value);
}
