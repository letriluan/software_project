using System.Net;
using System.Net.Http.Json;
using TutorDesk.Api.Features.Admin;
using TutorDesk.Api.Features.Lessons;
using TutorDesk.Api.Tests.Infrastructure;
using static TutorDesk.Api.Tests.Infrastructure.ApiHelpers;

namespace TutorDesk.Api.Tests.Integration;

public class AdminTests(TutorDeskFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Admin_creates_tutors_and_duplicate_emails_conflict()
    {
        var admin = await Factory.AdminAsync();
        var email = $"new-{Guid.NewGuid():N}@test.local";
        var request = new CreateTutorRequest { FullName = "New Tutor", Email = email, TemporaryPassword = "Temp!2345" };

        var created = await (await admin.PostAsJsonAsync("/api/admin/tutors", request))
            .ReadAsync<TutorSummary>(HttpStatusCode.Created);
        Assert.Equal(("New Tutor", email, true), (created.FullName, created.Email, created.IsActive));

        var duplicate = await admin.PostAsJsonAsync("/api/admin/tutors", request with { Email = email.ToUpperInvariant() });
        await duplicate.ProblemAsync(HttpStatusCode.Conflict);

        var weak = await admin.PostAsJsonAsync("/api/admin/tutors",
            request with { Email = $"weak-{Guid.NewGuid():N}@test.local", TemporaryPassword = "password" });
        await weak.ProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tutor_list_includes_workload_stats()
    {
        var tutor = await Factory.NewTutorAsync("Stats Tutor");
        var s1 = await tutor.Client.CreateStudentAsync();
        await tutor.Client.CreateStudentAsync("Second", "Student");
        await tutor.Client.BookAsync(s1.Id, Adelaide(2026, 11, 9, 16), repeatWeekly: 2);

        var list = await (await (await Factory.AdminAsync()).GetAsync("/api/admin/tutors"))
            .ReadAsync<List<TutorSummary>>(HttpStatusCode.OK);
        var row = Assert.Single(list, t => t.Id == tutor.Id);
        Assert.Equal((2, 2, 0.0), (row.ActiveStudents, row.UpcomingLessons, row.HoursThisMonth));
    }

    [Fact]
    public async Task Monthly_report_uses_adelaide_month_boundaries()
    {
        var tutor = await Factory.NewTutorAsync("Report Tutor");
        var student = await tutor.Client.CreateStudentAsync();
        var other = await tutor.Client.CreateStudentAsync("Other", "Kid");

        // 30 Nov 9pm local = 30 Nov 10:30 UTC  -> November
        var lateNov = await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 11, 30, 20, 30), 90);
        // 1 Dec 7am local = 30 Nov 20:30 UTC (still November in UTC!) -> December
        var earlyDec = await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 12, 1, 7), 60);
        var noShow = await tutor.Client.BookOneAsync(other.Id, Adelaide(2026, 11, 20, 16), 60);
        var cancelled = await tutor.Client.BookOneAsync(other.Id, Adelaide(2026, 11, 21, 10), 60);
        await tutor.Client.BookOneAsync(other.Id, Adelaide(2026, 11, 28, 10), 45); // stays scheduled

        await tutor.Client.PostAsJsonAsync($"/api/lessons/{cancelled.Id}/cancel",
            new CancelLessonRequest { Reason = "Unwell", Version = cancelled.Version });

        Clock.SetUtcNow(new DateTimeOffset(2026, 12, 2, 0, 0, 0, TimeSpan.Zero));
        (await tutor.Client.MarkAsync(lateNov, AttendanceOutcome.Completed, "Good", 4)).EnsureSuccessStatusCode();
        (await tutor.Client.MarkAsync(earlyDec, AttendanceOutcome.Completed)).EnsureSuccessStatusCode();
        (await tutor.Client.MarkAsync(noShow, AttendanceOutcome.NoShow)).EnsureSuccessStatusCode();

        var admin = await Factory.AdminAsync();
        var nov = await (await admin.GetAsync("/api/admin/reports/monthly?year=2026&month=11"))
            .ReadAsync<MonthlyReport>(HttpStatusCode.OK);
        var dec = await (await admin.GetAsync("/api/admin/reports/monthly?year=2026&month=12"))
            .ReadAsync<MonthlyReport>(HttpStatusCode.OK);

        var n = Assert.Single(nov.Tutors, t => t.TutorId == tutor.Id);
        Assert.Equal(1.5, n.HoursCompleted);
        Assert.Equal((1, 1, 1, 1, 1), (n.LessonsCompleted, n.NoShows, n.Cancelled, n.StillScheduled, n.StudentsTaught));
        Assert.Equal(0.5, n.AttendanceRate);

        var d = Assert.Single(dec.Tutors, t => t.TutorId == tutor.Id);
        Assert.Equal((1.0, 1), (d.HoursCompleted, d.LessonsCompleted));

        Assert.Equal(new DateTime(2026, 10, 31, 13, 30, 0, DateTimeKind.Utc), nov.RangeStartUtc);
        Assert.Equal(30, nov.Daily.Count);
        Assert.True(nov.Daily.Single(x => x.Date == new DateOnly(2026, 11, 30)).HoursCompleted >= 1.5);
    }

    [Fact]
    public async Task Monthly_report_csv_download()
    {
        var tutor = await Factory.NewTutorAsync("=Csv Tutor");
        var admin = await Factory.AdminAsync();

        var response = await admin.GetAsync("/api/admin/reports/monthly.csv?year=2026&month=11");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("tutordesk-2026-11.csv", response.Content.Headers.ContentDisposition?.FileNameStar
                                              ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("tutor,active,hours_completed", csv.TrimStart('﻿'), StringComparison.Ordinal);
        Assert.Contains("\"'=Csv Tutor\",yes,0.00", csv, StringComparison.Ordinal);
        _ = tutor;
    }

    [Fact]
    public async Task Report_validates_month()
    {
        var admin = await Factory.AdminAsync();
        await (await admin.GetAsync("/api/admin/reports/monthly?year=2026&month=13")).ProblemAsync(HttpStatusCode.BadRequest);
    }
}

public class PlatformTests(TutorDeskFactory factory) : IntegrationTest(factory)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_are_up(string url)
    {
        var response = await Factory.Anonymous().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_api_route_is_a_problem_404_not_the_spa()
    {
        var tutor = await Factory.NewTutorAsync();
        var problem = await (await tutor.Client.GetAsync("/api/does-not-exist")).ProblemAsync(HttpStatusCode.NotFound);
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        var response = await Factory.Anonymous().GetAsync("/health/live");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subjects_are_seeded_by_the_migration()
    {
        var tutor = await Factory.NewTutorAsync();
        var subjects = await (await tutor.Client.GetAsync("/api/subjects"))
            .ReadAsync<List<TutorDesk.Api.Features.Subjects.SubjectDto>>(HttpStatusCode.OK);
        Assert.Contains(subjects, s => s.Name == "Mathematics");
        Assert.Equal(10, subjects.Count);
    }
}
