using TutorDesk.Api.Common;

namespace TutorDesk.Api.Features.Lessons;

/// <summary>Pure scheduling rules - no database, easy to unit test.</summary>
public static class LessonRules
{
    public const int MinDuration = 15;
    public const int MaxDuration = 240;
    public const int DurationStep = 15;
    public const int MaxWeeklyOccurrences = 12;
    public static readonly TimeOnly EarliestStart = new(6, 0);
    public static readonly TimeOnly LatestEnd = new(22, 0);

    public static IEnumerable<string> DurationErrors(int minutes)
    {
        if (minutes is < MinDuration or > MaxDuration)
            yield return $"Duration must be between {MinDuration} and {MaxDuration} minutes.";
        else if (minutes % DurationStep != 0)
            yield return $"Duration must be a multiple of {DurationStep} minutes.";
    }

    /// <summary>Lessons must start at or after 06:00 and finish by 22:00, Adelaide time.</summary>
    public static string? BusinessHoursError(DateTime startUtc, int durationMinutes)
    {
        var startLocal = AdelaideTime.ToLocal(startUtc);
        var endLocal = AdelaideTime.ToLocal(startUtc.AddMinutes(durationMinutes));
        var start = TimeOnly.FromDateTime(startLocal);
        if (start < EarliestStart || endLocal.Date != startLocal.Date ||
            TimeOnly.FromDateTime(endLocal) > LatestEnd)
        {
            return "Lessons must be between 06:00 and 22:00 (Adelaide time).";
        }
        return null;
    }

    /// <summary>
    /// Weekly occurrences that keep the same Adelaide wall-clock time, so a 4:00pm lesson stays at
    /// 4:00pm when daylight saving starts or ends (its UTC time shifts by an hour).
    /// </summary>
    public static IReadOnlyList<DateTime> ExpandWeekly(DateTime firstStartUtc, int occurrences)
    {
        if (occurrences is < 1 or > MaxWeeklyOccurrences)
            throw new ArgumentOutOfRangeException(nameof(occurrences));

        var firstLocal = AdelaideTime.ToLocal(firstStartUtc);
        return Enumerable.Range(0, occurrences)
            .Select(week => AdelaideTime.ToUtc(firstLocal.AddDays(7 * week)))
            .ToList();
    }

    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) =>
        aStart < bEnd && bStart < aEnd;
}
