using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TutorDesk.Api.Domain;
using TutorDesk.Api.Features.Lessons;
using TutorDesk.Api.Features.Students;

namespace TutorDesk.Api.Tests.Infrastructure;

public static class ApiHelpers
{
    /// <summary>Adelaide is UTC+10:30 in summer (Oct-Apr) and UTC+9:30 in winter.</summary>
    public static readonly TimeSpan AcdtOffset = TimeSpan.FromHours(10.5);

    public static DateTimeOffset Adelaide(int year, int month, int day, int hour, int minute = 0) =>
        new(new DateTime(year, month, day, hour, minute, 0),
            TutorDesk.Api.Common.AdelaideTime.Zone.GetUtcOffset(new DateTime(year, month, day, hour, minute, 0)));

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected} but got {(int)response.StatusCode}: {body}");
        }
        return (await response.Content.ReadFromJsonAsync<T>(TutorDeskFactory.Json))!;
    }

    public static async Task<JsonElement> ProblemAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    public static async Task<StudentDetail> CreateStudentAsync(
        this HttpClient client, string first = "Ava", string last = "Nguyen", int year = 10)
    {
        var response = await client.PostAsJsonAsync("/api/students", new StudentUpsertRequest
        {
            FirstName = first, LastName = last, YearLevel = year, School = "Adelaide High School",
            ParentEmail = "parent@example.com",
        });
        return await response.ReadAsync<StudentDetail>(HttpStatusCode.Created);
    }

    public static Task<HttpResponseMessage> BookAsync(
        this HttpClient client, Guid studentId, DateTimeOffset startsAt, int minutes = 60, int repeatWeekly = 1,
        int subjectId = 1) =>
        client.PostAsJsonAsync("/api/lessons", new BookLessonRequest
        {
            StudentId = studentId, SubjectId = subjectId, StartsAt = startsAt, DurationMinutes = minutes,
            Mode = LessonMode.InPerson, RepeatWeekly = repeatWeekly,
        }, TutorDeskFactory.Json);

    public static async Task<LessonDto> BookOneAsync(this HttpClient client, Guid studentId, DateTimeOffset startsAt,
        int minutes = 60)
    {
        var result = await (await client.BookAsync(studentId, startsAt, minutes))
            .ReadAsync<BookingResult>(HttpStatusCode.Created);
        return result.Lessons.Single();
    }

    public static Task<HttpResponseMessage> MarkAsync(this HttpClient client, LessonDto lesson,
        AttendanceOutcome outcome, string? note = null, int? rating = null) =>
        client.PostAsJsonAsync($"/api/lessons/{lesson.Id}/attendance", new MarkAttendanceRequest
        {
            Outcome = outcome, ProgressNote = note, UnderstandingRating = rating, Version = lesson.Version,
        }, TutorDeskFactory.Json);
}
