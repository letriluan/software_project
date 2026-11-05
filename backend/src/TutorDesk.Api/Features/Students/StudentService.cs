using Microsoft.EntityFrameworkCore;
using TutorDesk.Api.Auth;
using TutorDesk.Api.Common;
using TutorDesk.Api.Data;
using TutorDesk.Api.Domain;

namespace TutorDesk.Api.Features.Students;

public sealed class StudentService(AppDbContext db, ICurrentUser me, TimeProvider clock)
{
    /// <summary>
    /// Tutors only ever see their own students; admins see everyone.
    /// Other tutors' students return 404 (not 403) so their existence isn't leaked.
    /// </summary>
    internal IQueryable<Student> Visible() =>
        me.IsAdmin ? db.Students : db.Students.Where(s => s.TutorId == me.Id);

    public async Task<PagedResult<StudentListItem>> ListAsync(StudentQuery q, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var query = Visible();
        if (!q.IncludeArchived) query = query.Where(s => !s.IsArchived);
        if (me.IsAdmin && q.TutorId is { } tutorId) query = query.Where(s => s.TutorId == tutorId);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = $"%{q.Search.Trim().Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.FirstName + " " + s.LastName, pattern) ||
                EF.Functions.ILike(s.School ?? "", pattern) ||
                EF.Functions.ILike(s.ParentName ?? "", pattern));
        }

        return await query
            .OrderBy(s => s.IsArchived).ThenBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new StudentListItem(
                s.Id, s.FirstName, s.LastName, s.YearLevel, s.School, s.IsArchived,
                s.TutorId, s.Tutor!.FullName,
                s.Lessons.Where(l => l.Status == LessonStatus.Scheduled && l.StartsAtUtc >= now)
                    .Min(l => (DateTime?)l.StartsAtUtc),
                s.Lessons.Count(l => l.Status == LessonStatus.Completed)))
            .ToPagedAsync(q, ct);
    }

    public async Task<StudentDetail> GetAsync(Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var s = await Visible().Include(x => x.Tutor).AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Student", id);

        var lessons = await db.Lessons.Where(l => l.StudentId == id)
            .Select(l => new { l.Status, l.DurationMinutes, l.StartsAtUtc })
            .ToListAsync(ct);

        var completed = lessons.Count(l => l.Status == LessonStatus.Completed);
        var noShows = lessons.Count(l => l.Status == LessonStatus.NoShow);
        var stats = new StudentStats(
            completed,
            noShows,
            lessons.Count(l => l.Status == LessonStatus.Cancelled),
            lessons.Count(l => l.Status == LessonStatus.Scheduled && l.StartsAtUtc >= now),
            Math.Round(lessons.Where(l => l.Status == LessonStatus.Completed)
                .Sum(l => l.DurationMinutes) / 60.0, 2),
            completed + noShows == 0 ? null : Math.Round((double)completed / (completed + noShows), 3));

        return new StudentDetail(s.Id, s.FirstName, s.LastName, s.YearLevel, s.School, s.ParentName,
            s.ParentEmail, s.ParentPhone, s.Notes, s.IsArchived, s.TutorId, s.Tutor?.FullName ?? "",
            s.CreatedAtUtc, s.Version, stats);
    }

    public async Task<StudentDetail> CreateAsync(StudentUpsertRequest r, CancellationToken ct)
    {
        if (me.IsAdmin)
            throw new BusinessRuleException("Students belong to a tutor. Sign in as the tutor to add one.");

        var student = new Student { Id = Guid.NewGuid(), TutorId = me.Id };
        Apply(student, r);
        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
        return await GetAsync(student.Id, ct);
    }

    public async Task<StudentDetail> UpdateAsync(Guid id, UpdateStudentRequest r, CancellationToken ct)
    {
        var student = await Owned(id, ct);
        db.Entry(student).Property(s => s.Version).OriginalValue = r.Version!.Value;
        Apply(student, r);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Archives the student and cancels their future scheduled lessons.</summary>
    public async Task<ArchiveResult> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var student = await Owned(id, ct);
        if (student.IsArchived) return new ArchiveResult(0);

        var now = clock.GetUtcNow().UtcDateTime;
        var future = await db.Lessons
            .Where(l => l.StudentId == id && l.Status == LessonStatus.Scheduled && l.StartsAtUtc > now)
            .ToListAsync(ct);
        foreach (var lesson in future)
        {
            lesson.Status = LessonStatus.Cancelled;
            lesson.CancellationReason = "Student archived";
        }
        student.IsArchived = true;
        await db.SaveChangesAsync(ct);
        return new ArchiveResult(future.Count);
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct)
    {
        var student = await Owned(id, ct);
        student.IsArchived = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<StudentProgress> ProgressAsync(Guid id, int? subjectId, CancellationToken ct)
    {
        if (!await Visible().AnyAsync(s => s.Id == id, ct)) throw new NotFoundException("Student", id);

        var query = db.ProgressNotes.AsNoTracking().Where(p => p.StudentId == id);
        if (subjectId is { } sid) query = query.Where(p => p.SubjectId == sid);

        var notes = await query
            // Order by when the lesson happened (falling back to when the note was written).
            .OrderByDescending(p => p.Lesson != null ? p.Lesson.StartsAtUtc : p.CreatedAtUtc)
            .ThenByDescending(p => p.CreatedAtUtc)
            .Select(p => new ProgressNoteDto(p.Id, p.LessonId, p.SubjectId, p.Subject!.Name, p.Note,
                p.UnderstandingRating, p.CreatedAtUtc,
                p.Lesson != null ? (DateTime?)p.Lesson.StartsAtUtc : null))
            .ToListAsync(ct);

        var subjects = notes
            .GroupBy(n => (n.SubjectId, n.SubjectName))
            .Select(g =>
            {
                var rated = g.Where(n => n.UnderstandingRating is not null)
                    .OrderBy(n => n.LessonStartsAtUtc ?? n.CreatedAtUtc).Select(n => n.UnderstandingRating!.Value).ToList();
                return new SubjectProgressSummary(
                    g.Key.SubjectId, g.Key.SubjectName, g.Count(),
                    rated.Count == 0 ? null : Math.Round(rated.Average(), 2),
                    rated.Count == 0 ? null : rated[^1],
                    ProgressTrend.From(rated));
            })
            .OrderBy(s => s.SubjectName)
            .ToList();

        return new StudentProgress(subjects, notes);
    }

    /// <summary>Loads a student the current tutor may modify (admins are read-only for students).</summary>
    private async Task<Student> Owned(Guid id, CancellationToken ct) =>
        await db.Students.SingleOrDefaultAsync(s => s.Id == id && s.TutorId == me.Id, ct)
        ?? throw new NotFoundException("Student", id);

    private static void Apply(Student s, StudentUpsertRequest r)
    {
        s.FirstName = r.FirstName.Trim();
        s.LastName = r.LastName.Trim();
        s.YearLevel = r.YearLevel;
        s.School = Clean(r.School);
        s.ParentName = Clean(r.ParentName);
        s.ParentEmail = Clean(r.ParentEmail)?.ToLowerInvariant();
        s.ParentPhone = Clean(r.ParentPhone);
        s.Notes = Clean(r.Notes);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ProgressTrend
{
    /// <summary>
    /// Compares the average of the last three ratings with the three before them.
    /// Needs at least four ratings to say anything.
    /// </summary>
    public static string From(IReadOnlyList<int> chronologicalRatings)
    {
        if (chronologicalRatings.Count < 4) return "not enough data";
        var recent = chronologicalRatings.TakeLast(3).Average();
        var earlier = chronologicalRatings.SkipLast(3).TakeLast(3).Average();
        var delta = recent - earlier;
        return delta switch
        {
            >= 0.5 => "improving",
            <= -0.5 => "declining",
            _ => "steady",
        };
    }
}
