using System.Net;
using System.Net.Http.Json;
using TutorDesk.Api.Common;
using TutorDesk.Api.Domain;
using TutorDesk.Api.Features.Lessons;
using TutorDesk.Api.Features.Students;
using TutorDesk.Api.Tests.Infrastructure;
using static TutorDesk.Api.Tests.Infrastructure.ApiHelpers;

namespace TutorDesk.Api.Tests.Integration;

public class StudentTests(TutorDeskFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Tutor_can_create_and_read_a_student()
    {
        var tutor = await Factory.NewTutorAsync("Owner Tutor");
        var created = await tutor.Client.CreateStudentAsync("Mia", "Pham", 12);

        var fetched = await (await tutor.Client.GetAsync($"/api/students/{created.Id}"))
            .ReadAsync<StudentDetail>(HttpStatusCode.OK);

        Assert.Equal(("Mia", "Pham", 12, "Owner Tutor"),
            (fetched.FirstName, fetched.LastName, fetched.YearLevel, fetched.TutorName));
        Assert.Equal(0, fetched.Stats.CompletedLessons);
        Assert.Null(fetched.Stats.AttendanceRate);
    }

    [Fact]
    public async Task Invalid_student_returns_field_errors()
    {
        var tutor = await Factory.NewTutorAsync();
        var response = await tutor.Client.PostAsJsonAsync("/api/students", new StudentUpsertRequest
        {
            FirstName = "", LastName = "Smith", YearLevel = 13, ParentEmail = "not-an-email",
        });

        var errors = (await response.ProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("FirstName", out _));
        Assert.True(errors.TryGetProperty("YearLevel", out _));
        Assert.True(errors.TryGetProperty("ParentEmail", out _));
    }

    [Fact]
    public async Task Search_matches_name_and_school_case_insensitively()
    {
        var tutor = await Factory.NewTutorAsync();
        await tutor.Client.CreateStudentAsync("Oliver", "Smith");
        await tutor.Client.CreateStudentAsync("Chloe", "Tran");

        var page = await (await tutor.Client.GetAsync("/api/students?search=oliv"))
            .ReadAsync<PagedResult<StudentListItem>>(HttpStatusCode.OK);
        Assert.Equal("Oliver", Assert.Single(page.Items).FirstName);

        var bySchool = await (await tutor.Client.GetAsync("/api/students?search=ADELAIDE%20HIGH"))
            .ReadAsync<PagedResult<StudentListItem>>(HttpStatusCode.OK);
        Assert.Equal(2, bySchool.TotalCount);
    }

    [Fact]
    public async Task List_is_paged()
    {
        var tutor = await Factory.NewTutorAsync();
        for (var i = 0; i < 5; i++) await tutor.Client.CreateStudentAsync($"Kid{i}", "Paging");

        var page = await (await tutor.Client.GetAsync("/api/students?page=2&pageSize=2"))
            .ReadAsync<PagedResult<StudentListItem>>(HttpStatusCode.OK);

        Assert.Equal((2, 5, 3), (page.Items.Count, page.TotalCount, page.TotalPages));
    }

    [Fact]
    public async Task Tutors_cannot_see_or_change_each_others_students()
    {
        var alice = await Factory.NewTutorAsync();
        var bob = await Factory.NewTutorAsync();
        var student = await alice.Client.CreateStudentAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/students/{student.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.DeleteAsync($"/api/students/{student.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.PutAsJsonAsync($"/api/students/{student.Id}",
            new UpdateStudentRequest { FirstName = "X", LastName = "Y", YearLevel = 5, Version = student.Version })).StatusCode);

        var bobsList = await (await bob.Client.GetAsync("/api/students"))
            .ReadAsync<PagedResult<StudentListItem>>(HttpStatusCode.OK);
        Assert.DoesNotContain(bobsList.Items, s => s.Id == student.Id);

        // Bob can't book Alice's student either.
        var booking = await bob.Client.BookAsync(student.Id, Adelaide(2026, 11, 10, 16));
        Assert.Equal(HttpStatusCode.NotFound, booking.StatusCode);
    }

    [Fact]
    public async Task Admin_can_read_every_tutors_students_but_not_create()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        var admin = await Factory.AdminAsync();

        var list = await (await admin.GetAsync($"/api/students?tutorId={tutor.Id}"))
            .ReadAsync<PagedResult<StudentListItem>>(HttpStatusCode.OK);
        Assert.Equal(student.Id, Assert.Single(list.Items).Id);

        var create = await admin.PostAsJsonAsync("/api/students",
            new StudentUpsertRequest { FirstName = "A", LastName = "B", YearLevel = 3 });
        await create.ProblemAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Stale_version_returns_409()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        UpdateStudentRequest Edit(string name, uint version) =>
            new() { FirstName = name, LastName = "Nguyen", YearLevel = 11, Version = version };

        var first = await (await tutor.Client.PutAsJsonAsync($"/api/students/{student.Id}", Edit("Ava-Rose", student.Version)))
            .ReadAsync<StudentDetail>(HttpStatusCode.OK);
        Assert.NotEqual(student.Version, first.Version);

        // A second editor still holding the original version.
        var stale = await tutor.Client.PutAsJsonAsync($"/api/students/{student.Id}", Edit("Avery", student.Version));
        await stale.ProblemAsync(HttpStatusCode.Conflict);

        var current = await (await tutor.Client.GetAsync($"/api/students/{student.Id}"))
            .ReadAsync<StudentDetail>(HttpStatusCode.OK);
        Assert.Equal("Ava-Rose", current.FirstName);
    }

    [Fact]
    public async Task Archiving_cancels_future_lessons_and_blocks_new_bookings()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        await (await tutor.Client.BookAsync(student.Id, Adelaide(2026, 11, 10, 16), repeatWeekly: 3))
            .ReadAsync<BookingResult>(HttpStatusCode.Created);

        var archived = await (await tutor.Client.DeleteAsync($"/api/students/{student.Id}"))
            .ReadAsync<ArchiveResult>(HttpStatusCode.OK);
        Assert.Equal(3, archived.CancelledFutureLessons);

        var list = await (await tutor.Client.GetAsync("/api/students"))
            .ReadAsync<PagedResult<StudentListItem>>(HttpStatusCode.OK);
        Assert.DoesNotContain(list.Items, s => s.Id == student.Id);

        var booking = await tutor.Client.BookAsync(student.Id, Adelaide(2026, 12, 1, 16));
        await booking.ProblemAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal(HttpStatusCode.NoContent,
            (await tutor.Client.PostAsync($"/api/students/{student.Id}/restore", null)).StatusCode);
        await tutor.Client.BookOneAsync(student.Id, Adelaide(2026, 12, 1, 16));
    }

    [Fact]
    public async Task Progress_summarises_ratings_per_subject()
    {
        var tutor = await Factory.NewTutorAsync();
        var student = await tutor.Client.CreateStudentAsync();
        var lessons = (await (await tutor.Client.BookAsync(student.Id, Adelaide(2026, 11, 3, 16), repeatWeekly: 6))
            .ReadAsync<BookingResult>(HttpStatusCode.Created)).Lessons;

        Clock.SetUtcNow(new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero));
        var ratings = new[] { 2, 2, 2, 4, 4, 5 };
        for (var i = 0; i < lessons.Count; i++)
        {
            (await tutor.Client.MarkAsync(lessons[i], AttendanceOutcome.Completed, $"Lesson {i + 1}", ratings[i]))
                .EnsureSuccessStatusCode();
        }

        var progress = await (await tutor.Client.GetAsync($"/api/students/{student.Id}/progress"))
            .ReadAsync<StudentProgress>(HttpStatusCode.OK);

        var maths = Assert.Single(progress.Subjects);
        Assert.Equal(6, maths.Notes);
        Assert.Equal(5, maths.LatestRating);
        Assert.Equal("improving", maths.Trend);
        Assert.Equal("Lesson 6", progress.Notes[0].Note); // newest first

        var detail = await (await tutor.Client.GetAsync($"/api/students/{student.Id}"))
            .ReadAsync<StudentDetail>(HttpStatusCode.OK);
        Assert.Equal((6, 6.0, 1.0), (detail.Stats.CompletedLessons, detail.Stats.HoursTaught, detail.Stats.AttendanceRate));
    }
}
