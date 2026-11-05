using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TutorDesk.Api.Common;
using TutorDesk.Api.Domain;

namespace TutorDesk.Api.Data;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    public bool MigrateOnStartup { get; set; } = true;
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    /// <summary>Creates demo tutors, students and lessons (for the public demo and E2E tests).</summary>
    public bool DemoData { get; set; }
    public string DemoPassword { get; set; } = "Demo!2345";
}

public static class DemoAccounts
{
    public const string Admin = "admin@tutordesk.demo";
    public const string Tutor = "tutor@tutordesk.demo";
    public const string SecondTutor = "minh@tutordesk.demo";
}

public sealed class DbInitializer(
    AppDbContext db,
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    TimeProvider clock,
    ILogger<DbInitializer> logger)
{
    public async Task InitializeAsync(SeedOptions options, CancellationToken ct = default)
    {
        if (options.MigrateOnStartup)
        {
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("Database migrated");
        }

        foreach (var role in new[] { Roles.Admin, Roles.Tutor })
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
        }

        if (!string.IsNullOrWhiteSpace(options.AdminEmail) && !string.IsNullOrWhiteSpace(options.AdminPassword))
            await EnsureUserAsync(options.AdminEmail, "Administrator", options.AdminPassword, Roles.Admin);

        if (options.DemoData) await SeedDemoAsync(options.DemoPassword, ct);
    }

    public async Task<AppUser> EnsureUserAsync(string email, string fullName, string password, string role)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is not null) return user;

        user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Could not create {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        await users.AddToRoleAsync(user, role);
        logger.LogInformation("Created {Role} account {Email}", role, email);
        return user;
    }

    private async Task SeedDemoAsync(string password, CancellationToken ct)
    {
        await EnsureUserAsync(DemoAccounts.Admin, "Demo Admin", password, Roles.Admin);
        var tutor = await EnsureUserAsync(DemoAccounts.Tutor, "Luan Le", password, Roles.Tutor);
        var second = await EnsureUserAsync(DemoAccounts.SecondTutor, "Minh Tran", password, Roles.Tutor);

        if (await db.Students.AnyAsync(ct)) return;

        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Name, s => s.Id, ct);
        var rng = new Random(20261105); // deterministic demo data

        var roster = new (AppUser Tutor, string First, string Last, int Year, string School, string Subject)[]
        {
            (tutor, "Ava", "Nguyen", 11, "Glenunga International High School", "Mathematics"),
            (tutor, "Oliver", "Smith", 9, "Adelaide High School", "Science"),
            (tutor, "Mia", "Pham", 12, "Marryatville High School", "Specialist Mathematics"),
            (tutor, "Jack", "Wilson", 8, "Unley High School", "English"),
            (tutor, "Chloe", "Tran", 10, "Brighton Secondary School", "Chemistry"),
            (tutor, "Noah", "Brown", 12, "Norwood International High School", "Physics"),
            (second, "Isla", "Taylor", 7, "Prince Alfred College", "Mathematics"),
            (second, "Leo", "Vo", 11, "Woodville High School", "Computer Science"),
            (second, "Ruby", "Martin", 9, "Seaton High School", "English"),
        };

        var today = AdelaideTime.Today(clock);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // Monday
        var now = clock.GetUtcNow().UtcDateTime;
        var slotHours = new[] { 16, 17, 18, 19 };

        for (var i = 0; i < roster.Length; i++)
        {
            var (owner, first, last, year, school, subject) = roster[i];
            var student = new Student
            {
                Id = Guid.NewGuid(), TutorId = owner.Id, FirstName = first, LastName = last,
                YearLevel = year, School = school, ParentName = $"Parent of {first}",
                ParentEmail = $"{first.ToLowerInvariant()}.parent@example.com",
                ParentPhone = $"04{rng.Next(10000000, 99999999)}",
            };
            db.Students.Add(student);

            // One weekly lesson: 6 weeks back, 3 weeks ahead, same weekday/time.
            var weekday = i % 5; // Mon-Fri
            var hour = slotHours[i % slotHours.Length];
            var ratings = new List<int>();
            for (var w = -6; w <= 3; w++)
            {
                var local = weekStart.AddDays(7 * w + weekday).ToDateTime(new TimeOnly(hour, 0));
                var startUtc = AdelaideTime.ToUtc(local);
                var lesson = new Lesson
                {
                    Id = Guid.NewGuid(), TutorId = owner.Id, StudentId = student.Id,
                    SubjectId = subjects[subject], StartsAtUtc = startUtc, EndsAtUtc = startUtc.AddMinutes(60),
                    DurationMinutes = 60, Mode = i % 3 == 0 ? LessonMode.Online : LessonMode.InPerson,
                    Location = i % 3 == 0 ? "Google Meet" : "Student's home",
                };
                // Last week's lessons for the first three students stay unmarked, so the
                // dashboard always has something "still to mark" (the E2E tests rely on it).
                if (startUtc.AddHours(1) < now && !(w == -1 && i < 3))
                {
                    var roll = rng.NextDouble();
                    if (roll < 0.08)
                    {
                        lesson.Status = LessonStatus.Cancelled;
                        lesson.CancellationReason = "Family holiday";
                    }
                    else if (roll < 0.14)
                    {
                        lesson.Status = LessonStatus.NoShow;
                        lesson.AttendanceMarkedAtUtc = startUtc.AddHours(1);
                    }
                    else if (w < -1 || roll < 0.7) // leave a few recent ones unmarked for the dashboard
                    {
                        lesson.Status = LessonStatus.Completed;
                        lesson.AttendanceMarkedAtUtc = startUtc.AddHours(1);
                        var rating = Math.Clamp(2 + (w + 6) / 3 + rng.Next(-1, 2), 1, 5);
                        ratings.Add(rating);
                        db.ProgressNotes.Add(new ProgressNote
                        {
                            Id = Guid.NewGuid(), StudentId = student.Id, TutorId = owner.Id, LessonId = lesson.Id,
                            SubjectId = lesson.SubjectId, UnderstandingRating = rating,
                            Note = DemoNotes[rng.Next(DemoNotes.Length)], CreatedAtUtc = startUtc.AddHours(1),
                        });
                    }
                }
                db.Lessons.Add(lesson);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo data: {Students} students", roster.Length);
    }

    private static readonly string[] DemoNotes =
    [
        "Worked through past exam questions; strong on algebra, needs practice with word problems.",
        "Revised key definitions. Homework: chapter review questions 1-10.",
        "Good progress on essay structure; topic sentences are much clearer.",
        "Struggled with the new topic at first, understood it after worked examples.",
        "Practice test under timed conditions - finished with 10 minutes to spare.",
        "Focused on exam technique and showing working for full marks.",
    ];
}

/// <summary>Lets `dotnet ef` create the context without running the web host.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=tutordesk;Username=postgres;Password=postgres")
            .Options);
}
