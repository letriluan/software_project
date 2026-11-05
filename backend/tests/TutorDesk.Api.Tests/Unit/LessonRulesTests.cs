using TutorDesk.Api.Common;
using TutorDesk.Api.Features.Lessons;

namespace TutorDesk.Api.Tests.Unit;

public class LessonRulesTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(240)]
    public void Valid_durations_have_no_errors(int minutes) =>
        Assert.Empty(LessonRules.DurationErrors(minutes));

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(255)]
    [InlineData(50)]
    public void Invalid_durations_are_rejected(int minutes) =>
        Assert.Single(LessonRules.DurationErrors(minutes));

    [Fact]
    public void Lesson_inside_business_hours_is_allowed()
    {
        // 4pm Adelaide (ACDT, +10:30) = 05:30 UTC
        var start = new DateTime(2026, 11, 10, 5, 30, 0, DateTimeKind.Utc);
        Assert.Null(LessonRules.BusinessHoursError(start, 60));
    }

    [Theory]
    [InlineData(2026, 11, 9, 19, 0, 60)]   // 5:30am local - too early
    [InlineData(2026, 11, 10, 11, 0, 60)]  // 9:30pm local, ends 10:30pm - too late
    [InlineData(2026, 11, 10, 10, 30, 90)] // 9:00pm + 90 min crosses 22:00
    public void Lesson_outside_business_hours_is_rejected(int y, int m, int d, int h, int min, int duration)
    {
        var start = new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc);
        Assert.NotNull(LessonRules.BusinessHoursError(start, duration));
    }

    [Fact]
    public void Lesson_ending_exactly_at_10pm_is_allowed()
    {
        // 9pm local (10:30 UTC) for 60 min ends 22:00
        Assert.Null(LessonRules.BusinessHoursError(new DateTime(2026, 11, 10, 10, 30, 0, DateTimeKind.Utc), 60));
    }

    [Fact]
    public void Weekly_series_keeps_local_time_when_daylight_saving_ends()
    {
        // Adelaide DST ends Sunday 4 April 2027 (clocks go from +10:30 to +9:30).
        // Monday 29 March 2027 4:00pm ACDT = 05:30 UTC.
        var first = new DateTime(2027, 3, 29, 5, 30, 0, DateTimeKind.Utc);

        var starts = LessonRules.ExpandWeekly(first, 3);

        Assert.Equal(first, starts[0]);
        Assert.Equal(new DateTime(2027, 4, 5, 6, 30, 0, DateTimeKind.Utc), starts[1]); // 4pm ACST
        Assert.Equal(new DateTime(2027, 4, 12, 6, 30, 0, DateTimeKind.Utc), starts[2]);
        Assert.All(starts, s => Assert.Equal(16, AdelaideTime.ToLocal(s).Hour));
    }

    [Fact]
    public void Weekly_series_keeps_local_time_when_daylight_saving_starts()
    {
        // DST starts Sunday 4 October 2026. Thursday 1 Oct 5pm ACST = 07:30 UTC.
        var starts = LessonRules.ExpandWeekly(new DateTime(2026, 10, 1, 7, 30, 0, DateTimeKind.Utc), 2);
        Assert.Equal(new DateTime(2026, 10, 8, 6, 30, 0, DateTimeKind.Utc), starts[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Weekly_series_length_is_bounded(int occurrences) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LessonRules.ExpandWeekly(new DateTime(2026, 11, 10, 5, 30, 0, DateTimeKind.Utc), occurrences));

    [Theory]
    [InlineData(10, 11, 10, 11, true)]  // identical
    [InlineData(10, 12, 11, 13, true)]  // partial
    [InlineData(10, 11, 11, 12, false)] // back-to-back is fine
    [InlineData(10, 11, 12, 13, false)]
    public void Overlap_detection(int aStart, int aEnd, int bStart, int bEnd, bool expected)
    {
        DateTime H(int h) => new(2026, 11, 10, h, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, LessonRules.Overlaps(H(aStart), H(aEnd), H(bStart), H(bEnd)));
        Assert.Equal(expected, LessonRules.Overlaps(H(bStart), H(bEnd), H(aStart), H(aEnd)));
    }
}
