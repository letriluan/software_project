namespace TutorDesk.Api.Common;

/// <summary>
/// The business runs on Adelaide time (UTC+9:30, +10:30 in daylight saving).
/// Everything is stored in UTC; conversions for "local" rules (weekly repeats,
/// monthly reports, "today") go through here.
/// </summary>
public static class AdelaideTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Adelaide");

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // A local time inside the DST "gap" does not exist; move it forward one hour.
        if (Zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    /// <summary>UTC range [start, end) covering one calendar month in Adelaide.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) MonthRangeUtc(int year, int month)
    {
        var startLocal = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (ToUtc(startLocal), ToUtc(startLocal.AddMonths(1)));
    }

    /// <summary>UTC range [start, end) covering one local day.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateOnly day)
    {
        var startLocal = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (ToUtc(startLocal), ToUtc(startLocal.AddDays(1)));
    }

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(ToLocal(clock.GetUtcNow().UtcDateTime));
}
