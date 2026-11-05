using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TutorDesk.Api.Auth;
using TutorDesk.Api.Common;
using TutorDesk.Api.Data;
using TutorDesk.Api.Domain;

namespace TutorDesk.Api.Features.Lessons;

public sealed class LessonService(
    AppDbContext db, ICurrentUser me, TimeProvider clock, ILogger<LessonService> logger)
{
    private const int MaxRangeDays = 62;

    internal static readonly Expression<Func<Lesson, LessonDto>> ToDto = l => new LessonDto(
        l.Id, l.TutorId, l.Tutor!.FullName, l.StudentId, l.Student!.FirstName + " " + l.Student.LastName,
        l.SubjectId, l.Subject!.Name, l.StartsAtUtc, l.EndsAtUtc, l.DurationMinutes, l.Mode, l.Location,
        l.Status, l.CancellationReason, l.SeriesId, l.Version,
        l.ProgressNote == null ? null : new LessonProgressDto(l.ProgressNote.Note, l.ProgressNote.UnderstandingRating));

    private IQueryable<Lesson> Visible() =>
        me.IsAdmin ? db.Lessons : db.Lessons.Where(l => l.TutorId == me.Id);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<LessonDto>> ListAsync(LessonQuery q, CancellationToken ct)
    {
        var from = q.From!.Value.UtcDateTime;
        var to = q.To!.Value.UtcDateTime;
        if (to <= from) throw RequestValidationException.For(nameof(q.To), "'to' must be after 'from'.");
        if ((to - from).TotalDays > MaxRangeDays)
            throw RequestValidationException.For(nameof(q.To), $"Range can be at most {MaxRangeDays} days.");

        var query = Visible().AsNoTracking().Where(l => l.StartsAtUtc < to && l.EndsAtUtc > from);
        if (q.StudentId is { } sid) query = query.Where(l => l.StudentId == sid);
        if (q.Status is { } status) query = query.Where(l => l.Status == status);
        if (me.IsAdmin && q.TutorId is { } tid) query = query.Where(l => l.TutorId == tid);

        return await query.OrderBy(l => l.StartsAtUtc).Select(ToDto).ToListAsync(ct);
    }

    public async Task<LessonDto> GetAsync(Guid id, CancellationToken ct) =>
        await Visible().AsNoTracking().Where(l => l.Id == id).Select(ToDto).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("Lesson", id);

    public async Task<BookingResult> BookAsync(BookLessonRequest r, CancellationToken ct)
    {
        if (me.IsAdmin) throw new BusinessRuleException("Only tutors can book lessons.");

        var duration = r.DurationMinutes!.Value;
        var firstStart = r.StartsAt!.Value.UtcDateTime;
        ValidateTiming(firstStart, duration, nameof(r.StartsAt));

        var student = await db.Students.SingleOrDefaultAsync(
                          s => s.Id == r.StudentId && s.TutorId == me.Id, ct)
                      ?? throw new NotFoundException("Student", r.StudentId!);
        if (student.IsArchived)
            throw new BusinessRuleException("This student is archived. Restore them before booking.");
        await EnsureSubjectExists(r.SubjectId!.Value, ct);

        var starts = LessonRules.ExpandWeekly(firstStart, r.RepeatWeekly);
        foreach (var start in starts.Skip(1))
        {
            // DST shifts can't push a repeat outside business hours, but check anyway.
            if (LessonRules.BusinessHoursError(start, duration) is { } err)
                throw RequestValidationException.For(nameof(r.StartsAt), err);
        }

        var slots = starts.Select(s => (Start: s, End: s.AddMinutes(duration))).ToList();
        var conflicts = await FindConflictsAsync(slots, me.Id, student.Id, excludeLessonId: null, ct);
        if (conflicts.Count > 0)
        {
            Telemetry.LessonConflicts.Add(1);
            throw new ConflictException(
                $"{conflicts.Count} of {slots.Count} requested time(s) overlap existing lessons.", conflicts);
        }

        Guid? seriesId = slots.Count > 1 ? Guid.NewGuid() : null;
        var lessons = slots.Select(slot => new Lesson
        {
            Id = Guid.NewGuid(),
            TutorId = me.Id,
            StudentId = student.Id,
            SubjectId = r.SubjectId!.Value,
            StartsAtUtc = slot.Start,
            EndsAtUtc = slot.End,
            DurationMinutes = duration,
            Mode = r.Mode,
            Location = string.IsNullOrWhiteSpace(r.Location) ? null : r.Location.Trim(),
            SeriesId = seriesId,
        }).ToList();

        db.Lessons.AddRange(lessons);
        await db.SaveChangesAsync(ct);
        Telemetry.LessonsBooked.Add(lessons.Count);
        logger.LogInformation("Tutor {TutorId} booked {Count} lesson(s) for student {StudentId}",
            me.Id, lessons.Count, student.Id);

        var ids = lessons.Select(l => l.Id).ToList();
        var dtos = await db.Lessons.AsNoTracking().Where(l => ids.Contains(l.Id))
            .OrderBy(l => l.StartsAtUtc).Select(ToDto).ToListAsync(ct);
        return new BookingResult(seriesId, dtos);
    }

    public async Task<LessonDto> RescheduleAsync(Guid id, RescheduleLessonRequest r, CancellationToken ct)
    {
        var lesson = await Owned(id, ct);
        if (lesson.Status != LessonStatus.Scheduled)
            throw new BusinessRuleException($"A {lesson.Status.ToString().ToLowerInvariant()} lesson can't be changed.");
        if (lesson.StartsAtUtc <= Now)
            throw new BusinessRuleException("This lesson has already started. Mark attendance instead.");

        var duration = r.DurationMinutes!.Value;
        var start = r.StartsAt!.Value.UtcDateTime;
        ValidateTiming(start, duration, nameof(r.StartsAt));
        await EnsureSubjectExists(r.SubjectId!.Value, ct);

        var conflicts = await FindConflictsAsync(
            [(start, start.AddMinutes(duration))], lesson.TutorId, lesson.StudentId, lesson.Id, ct);
        if (conflicts.Count > 0)
        {
            Telemetry.LessonConflicts.Add(1);
            throw new ConflictException("The new time overlaps another lesson.", conflicts);
        }

        db.Entry(lesson).Property(l => l.Version).OriginalValue = r.Version!.Value;
        lesson.StartsAtUtc = start;
        lesson.EndsAtUtc = start.AddMinutes(duration);
        lesson.DurationMinutes = duration;
        lesson.SubjectId = r.SubjectId!.Value;
        lesson.Mode = r.Mode;
        lesson.Location = string.IsNullOrWhiteSpace(r.Location) ? null : r.Location.Trim();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<CancelResult> CancelAsync(Guid id, CancelLessonRequest r, CancellationToken ct)
    {
        var lesson = await Owned(id, ct);
        if (lesson.Status != LessonStatus.Scheduled)
            throw new BusinessRuleException($"A {lesson.Status.ToString().ToLowerInvariant()} lesson can't be cancelled.");

        db.Entry(lesson).Property(l => l.Version).OriginalValue = r.Version!.Value;
        var toCancel = new List<Lesson> { lesson };
        if (r.IncludeFollowingInSeries && lesson.SeriesId is { } seriesId)
        {
            toCancel.AddRange(await db.Lessons
                .Where(l => l.SeriesId == seriesId && l.Id != lesson.Id &&
                            l.Status == LessonStatus.Scheduled && l.StartsAtUtc > lesson.StartsAtUtc)
                .ToListAsync(ct));
        }

        foreach (var l in toCancel)
        {
            l.Status = LessonStatus.Cancelled;
            l.CancellationReason = r.Reason.Trim();
        }
        await db.SaveChangesAsync(ct);
        return new CancelResult(toCancel.Count);
    }

    public async Task<LessonDto> MarkAttendanceAsync(Guid id, MarkAttendanceRequest r, CancellationToken ct)
    {
        var lesson = await Owned(id, ct);
        if (lesson.Status != LessonStatus.Scheduled)
            throw new ConflictException($"Attendance was already recorded as {lesson.Status}.");
        if (lesson.StartsAtUtc > Now)
            throw new BusinessRuleException("Attendance can only be marked once the lesson has started.");

        var outcome = r.Outcome!.Value;
        if (outcome == AttendanceOutcome.NoShow && r.UnderstandingRating is not null)
            throw RequestValidationException.For(nameof(r.UnderstandingRating),
                "A rating can only be given for a completed lesson.");

        db.Entry(lesson).Property(l => l.Version).OriginalValue = r.Version!.Value;
        lesson.Status = outcome == AttendanceOutcome.Completed ? LessonStatus.Completed : LessonStatus.NoShow;
        lesson.AttendanceMarkedAtUtc = Now;

        if (outcome == AttendanceOutcome.Completed &&
            (!string.IsNullOrWhiteSpace(r.ProgressNote) || r.UnderstandingRating is not null))
        {
            db.ProgressNotes.Add(new ProgressNote
            {
                Id = Guid.NewGuid(),
                StudentId = lesson.StudentId,
                TutorId = lesson.TutorId,
                LessonId = lesson.Id,
                SubjectId = lesson.SubjectId,
                Note = string.IsNullOrWhiteSpace(r.ProgressNote) ? "(rating only)" : r.ProgressNote.Trim(),
                UnderstandingRating = r.UnderstandingRating,
                CreatedAtUtc = Now,
            });
        }

        await db.SaveChangesAsync(ct);
        Telemetry.AttendanceMarked.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        return await GetAsync(id, ct);
    }

    private void ValidateTiming(DateTime startUtc, int duration, string field)
    {
        var errors = new Dictionary<string, string[]>();
        var durationErrors = LessonRules.DurationErrors(duration).ToArray();
        if (durationErrors.Length > 0) errors["DurationMinutes"] = durationErrors;
        if (startUtc <= Now) errors[field] = ["Lessons can't be booked in the past."];
        else if (durationErrors.Length == 0 && LessonRules.BusinessHoursError(startUtc, duration) is { } err)
            errors[field] = [err];
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private async Task EnsureSubjectExists(int subjectId, CancellationToken ct)
    {
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            throw RequestValidationException.For("SubjectId", "Unknown subject.");
    }

    private async Task<List<LessonConflict>> FindConflictsAsync(
        IReadOnlyList<(DateTime Start, DateTime End)> slots, Guid tutorId, Guid studentId,
        Guid? excludeLessonId, CancellationToken ct)
    {
        var min = slots.Min(s => s.Start);
        var max = slots.Max(s => s.End);
        var existing = await db.Lessons.AsNoTracking()
            .Where(l => l.Status != LessonStatus.Cancelled &&
                        (l.TutorId == tutorId || l.StudentId == studentId) &&
                        l.StartsAtUtc < max && l.EndsAtUtc > min &&
                        (excludeLessonId == null || l.Id != excludeLessonId))
            .Select(l => new { l.Id, l.TutorId, l.StudentId, l.StartsAtUtc, l.EndsAtUtc })
            .ToListAsync(ct);

        return (from slot in slots
                from l in existing
                where LessonRules.Overlaps(slot.Start, slot.End, l.StartsAtUtc, l.EndsAtUtc)
                select new LessonConflict(slot.Start, l.Id, l.StartsAtUtc,
                    l.TutorId == tutorId ? "tutor" : "student"))
            .ToList();
    }

    /// <summary>Lessons are changed only by the tutor who owns them.</summary>
    private async Task<Lesson> Owned(Guid id, CancellationToken ct) =>
        await db.Lessons.SingleOrDefaultAsync(l => l.Id == id && l.TutorId == me.Id, ct)
        ?? throw new NotFoundException("Lesson", id);
}
