using System.Net;
using System.Net.Http.Json;
using TutorDesk.Api.Domain;
using TutorDesk.Api.Features.Dashboard;
using TutorDesk.Api.Features.Lessons;
using TutorDesk.Api.Tests.Infrastructure;
using static TutorDesk.Api.Tests.Infrastructure.ApiHelpers;

namespace TutorDesk.Api.Tests.Integration;

public class LessonTests(TutorDeskFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Book_a_single_lesson()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync("Jack", "Wilson");

        var lesson = await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 10, 16), 90);

        Assert.Equal(new DateTime(2026, 11, 10, 5, 30, 0, DateTimeKind.Utc), lesson.StartsAtUtc);
        Assert.Equal(lesson.StartsAtUtc.AddMinutes(90), lesson.EndsAtUtc);
        Assert.Equal(("Jack Wilson", "Mathematics", LessonStatus.Scheduled, (Guid?)null),
            (lesson.StudentName, lesson.SubjectName, lesson.Status, lesson.SeriesId));
    }

    [Fact]
    public async Task Invalid_times_are_rejected_with_field_errors()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();

        var past = await tutor.Client.BookAsync(student.Id, Adelaide(2026, 11, 1, 16));
        Assert.True((await past.ProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("StartsAt", out _));

        var late = await tutor.Client.BookAsync(student.Id, Adelaide(2026, 11, 10, 21, 30));
        await late.ProblemAsync(HttpStatusCode.BadRequest);

        var oddDuration = await tutor.Client.BookAsync(student.Id, Adelaide(2026, 11, 10, 16), minutes: 50);
        Assert.True((await oddDuration.ProblemAsync(HttpStatusCode.BadRequest))
            .GetProperty("errors").TryGetProperty("DurationMinutes", out _));

        var badSubject = await tutor.Client.BookAsync(student.Id, Adelaide(2026, 11, 10, 16), subjectId: 999);
        await badSubject.ProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Overlapping_booking_returns_409_with_the_conflict()
    {
        var tutor = await Factory.NewTutorAsync();
        var a = await tutor.Client.CreateStudentAsync("A", "One");
        var b = await tutor.Client.CreateStudentAsync("B", "Two");
        var existing = await tutor.Client.BookOneAsync(a.Id, Adelaide(2026, 11, 10, 16));

        // Same tutor, different student, 30 minutes into the existing lesson.
        var response = await tutor.Client.BookAsync(b.Id, Adelaide(2026, 11, 10, 16, 30));
        var problem = await response.ProblemAsync(HttpStatusCode.Conflict);
        var conflict = problem.GetProperty("conflicts")[0];
        Assert.Equal(existing.Id, conflict.GetProperty("conflictingLessonId").GetGuid());
        Assert.Equal("tutor", conflict.GetProperty("conflictsWith").GetString());

        // Back-to-back is fine.
        await tutor.Client.BookOneAsync(b.Id, Adelaide(2026, 11, 10, 17));
    }

    [Fact]
    public async Task Different_tutors_can_teach_at_the_same_time()
    {
        var t1 = await Factory.NewTutorAsync();
        var t2 = await Factory.NewTutorAsync();
        await t1.Client.BookOneAsync((await t1.Client.CreateStudentAsync()).Id, Adelaide(2026, 11, 11, 16));
        await t2.Client.BookOneAsync((await t2.Client.CreateStudentAsync()).Id, Adelaide(2026, 11, 11, 16));
    }

    [Fact]
    public async Task Cancelled_lessons_free_the_slot()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        var lesson = await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 12, 16));

        await tutor.Client.PostAsJsonAsync($"/api/lessons/{lesson.Id}/cancel",
            new CancelLessonRequest { Reason = "Sick", Version = lesson.Version });

        await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 12, 16));
    }

    [Fact]
    public async Task Weekly_series_is_all_or_nothing()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 24, 16)); // week 3 of the series

        var series = await tutor.Client.BookAsync(student.Id, Adelaide(2026, 11, 10, 16), repeatWeekly: 4);
        var problem = await series.ProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal(1, problem.GetProperty("conflicts").GetArrayLength());

        var lessons = await (await tutor.Client.GetAsync(
                $"/api/lessons?from={Uri.EscapeDataString(Adelaide(2026, 11, 1, 0).ToString("O"))}" +
                $"&to={Uri.EscapeDataString(Adelaide(2026, 12, 31, 0).ToString("O"))}"))
            .ReadAsync<List<LessonDto>>(HttpStatusCode.OK);
        Assert.Single(lessons); // nothing from the series was created
    }

    [Fact]
    public async Task Weekly_series_keeps_4pm_across_the_end_of_daylight_saving()
    {
        Clock.SetUtcNow(new DateTimeOffset(2027, 3, 20, 0, 0, 0, TimeSpan.Zero));
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();

        var result = await (await tutor.Client.BookAsync(student.Id, Adelaide(2027, 3, 29, 16), repeatWeekly: 3))
            .ReadAsync<BookingResult>(HttpStatusCode.Created);

        Assert.NotNull(result.SeriesId);
        Assert.All(result.Lessons, l => Assert.Equal(result.SeriesId, l.SeriesId));
        var expected = new[]
        {
            new DateTime(2027, 3, 29, 5, 30, 0, DateTimeKind.Utc), // 4pm ACDT (+10:30)
            new DateTime(2027, 4, 5, 6, 30, 0, DateTimeKind.Utc),  // 4pm ACST (+9:30)
            new DateTime(2027, 4, 12, 6, 30, 0, DateTimeKind.Utc),
        };
        Assert.Equal(expected, result.Lessons.Select(l => l.StartsAtUtc).ToArray());
    }

    [Fact]
    public async Task Concurrent_double_booking_creates_exactly_one_lesson()
    {
        var tutor = await Factory.NewTutorAsync();
        var a = await tutor.Client.CreateStudentAsync("Race", "One");
        var b = await tutor.Client.CreateStudentAsync("Race", "Two");

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            tutor.Client.BookAsync(i % 2 == 0 ? a.Id : b.Id, Adelaide(2026, 11, 13, 16))));

        Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(attempts.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Reschedule_checks_conflicts_and_versions()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        var first = await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 16, 16));
        var second = await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 16, 18));

        RescheduleLessonRequest Move(DateTimeOffset to, uint version) =>
            new() { SubjectId = 1, StartsAt = to, DurationMinutes = 60, Version = version };

        var clash = await tutor.Client.PutAsJsonAsync($"/api/lessons/{second.Id}", Move(Adelaide(2026, 11, 16, 16, 30), second.Version));
        await clash.ProblemAsync(HttpStatusCode.Conflict);

        // Moving a lesson onto a slot overlapping only itself is fine.
        var moved = await (await tutor.Client.PutAsJsonAsync($"/api/lessons/{first.Id}", Move(Adelaide(2026, 11, 16, 16, 30), first.Version)))
            .ReadAsync<LessonDto>(HttpStatusCode.OK);
        Assert.Equal(new DateTime(2026, 11, 16, 6, 0, 0, DateTimeKind.Utc), moved.StartsAtUtc);

        var stale = await tutor.Client.PutAsJsonAsync($"/api/lessons/{first.Id}", Move(Adelaide(2026, 11, 16, 9), first.Version));
        await stale.ProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_can_include_the_rest_of_the_series()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        var lessons = (await (await tutor.Client.BookAsync(student.Id, Adelaide(2026, 11, 17, 16), repeatWeekly: 4))
            .ReadAsync<BookingResult>(HttpStatusCode.Created)).Lessons;

        var result = await (await tutor.Client.PostAsJsonAsync($"/api/lessons/{lessons[1].Id}/cancel",
                new CancelLessonRequest { Reason = "Term break", Version = lessons[1].Version, IncludeFollowingInSeries = true }))
            .ReadAsync<CancelResult>(HttpStatusCode.OK);
        Assert.Equal(3, result.CancelledCount);

        var firstStill = await (await tutor.Client.GetAsync($"/api/lessons/{lessons[0].Id}")).ReadAsync<LessonDto>(HttpStatusCode.OK);
        Assert.Equal(LessonStatus.Scheduled, firstStill.Status);
        var last = await (await tutor.Client.GetAsync($"/api/lessons/{lessons[3].Id}")).ReadAsync<LessonDto>(HttpStatusCode.OK);
        Assert.Equal((LessonStatus.Cancelled, "Term break"), (last.Status, last.CancellationReason));

        var again = await tutor.Client.PostAsJsonAsync($"/api/lessons/{lessons[1].Id}/cancel",
            new CancelLessonRequest { Reason = "Twice", Version = last.Version });
        await again.ProblemAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Attendance_rules()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        var lesson = await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 18, 16));

        // Too early.
        await (await tutor.Client.MarkAsync(lesson, AttendanceOutcome.Completed)).ProblemAsync(HttpStatusCode.UnprocessableEntity);

        Clock.SetUtcNow(new DateTimeOffset(2026, 11, 18, 6, 0, 0, TimeSpan.Zero)); // 4:30pm, lesson under way

        // Rating on a no-show makes no sense.
        await (await tutor.Client.MarkAsync(lesson, AttendanceOutcome.NoShow, rating: 3)).ProblemAsync(HttpStatusCode.BadRequest);

        var done = await (await tutor.Client.MarkAsync(lesson, AttendanceOutcome.Completed, "Quadratics - solid.", 4))
            .ReadAsync<LessonDto>(HttpStatusCode.OK);
        Assert.Equal(LessonStatus.Completed, done.Status);
        Assert.Equal(("Quadratics - solid.", (int?)4), (done.Progress!.Note, done.Progress.UnderstandingRating));

        // Only once.
        await (await tutor.Client.MarkAsync(done, AttendanceOutcome.NoShow)).ProblemAsync(HttpStatusCode.Conflict);

        // Completed lessons can't be moved.
        var move = await tutor.Client.PutAsJsonAsync($"/api/lessons/{lesson.Id}", new RescheduleLessonRequest
        {
            SubjectId = 1, StartsAt = Adelaide(2026, 11, 25, 16), DurationMinutes = 60, Version = done.Version,
        });
        await move.ProblemAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Range_query_validates_its_bounds()
    {
        var tutor = await Factory.NewTutorAsync();
        var tooLong = await tutor.Client.GetAsync("/api/lessons?from=2026-01-01T00:00:00Z&to=2026-06-01T00:00:00Z");
        await tooLong.ProblemAsync(HttpStatusCode.BadRequest);
        var reversed = await tutor.Client.GetAsync("/api/lessons?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z");
        await reversed.ProblemAsync(HttpStatusCode.BadRequest);
        var missing = await tutor.Client.GetAsync("/api/lessons");
        await missing.ProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dashboard_shows_today_and_lessons_needing_attendance()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        var morning = await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 19, 9));
        var evening = await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 19, 18));
        await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 20, 18));

        Clock.SetUtcNow(new DateTimeOffset(2026, 11, 19, 2, 0, 0, TimeSpan.Zero)); // 12:30pm on the 19th

        var dash = await (await tutor.Client.GetAsync("/api/dashboard")).ReadAsync<TutorDashboard>(HttpStatusCode.OK);
        Assert.Equal(new DateOnly(2026, 11, 19), dash.Today);
        Assert.Equal(new[] { morning.Id, evening.Id }, dash.TodaysLessons.Select(l => l.Id).ToArray());
        Assert.Equal(morning.Id, Assert.Single(dash.NeedsAttendance).Id);
        Assert.Equal(2, dash.UpcomingThisWeek);
        Assert.Equal(1, dash.ActiveStudents);
    }
}
