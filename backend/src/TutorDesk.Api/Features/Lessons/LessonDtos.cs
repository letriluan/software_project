using System.ComponentModel.DataAnnotations;
using TutorDesk.Api.Domain;

namespace TutorDesk.Api.Features.Lessons;

public sealed record BookLessonRequest
{
    [Required] public Guid? StudentId { get; init; }
    [Required] public int? SubjectId { get; init; }
    /// <summary>Start time with offset, e.g. 2026-11-10T16:00:00+10:30.</summary>
    [Required] public DateTimeOffset? StartsAt { get; init; }
    [Required] public int? DurationMinutes { get; init; }
    public LessonMode Mode { get; init; } = LessonMode.InPerson;
    [StringLength(200)] public string? Location { get; init; }
    /// <summary>1 = single lesson; up to 12 creates a weekly series.</summary>
    [Range(1, LessonRules.MaxWeeklyOccurrences)] public int RepeatWeekly { get; init; } = 1;
}

public sealed record RescheduleLessonRequest
{
    [Required] public int? SubjectId { get; init; }
    [Required] public DateTimeOffset? StartsAt { get; init; }
    [Required] public int? DurationMinutes { get; init; }
    public LessonMode Mode { get; init; } = LessonMode.InPerson;
    [StringLength(200)] public string? Location { get; init; }
    [Required] public uint? Version { get; init; }
}

public sealed record CancelLessonRequest
{
    [Required, StringLength(500, MinimumLength = 3)] public string Reason { get; init; } = "";
    [Required] public uint? Version { get; init; }
    /// <summary>Also cancel later scheduled lessons in the same weekly series.</summary>
    public bool IncludeFollowingInSeries { get; init; }
}

public enum AttendanceOutcome
{
    Completed,
    NoShow,
}

public sealed record MarkAttendanceRequest
{
    [Required] public AttendanceOutcome? Outcome { get; init; }
    [StringLength(2000)] public string? ProgressNote { get; init; }
    [Range(1, 5)] public int? UnderstandingRating { get; init; }
    [Required] public uint? Version { get; init; }
}

public sealed class LessonQuery
{
    [Required] public DateTimeOffset? From { get; init; }
    [Required] public DateTimeOffset? To { get; init; }
    public Guid? StudentId { get; init; }
    public LessonStatus? Status { get; init; }
    /// <summary>Admin only.</summary>
    public Guid? TutorId { get; init; }
}

public sealed record LessonDto(
    Guid Id,
    Guid TutorId,
    string TutorName,
    Guid StudentId,
    string StudentName,
    int SubjectId,
    string SubjectName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int DurationMinutes,
    LessonMode Mode,
    string? Location,
    LessonStatus Status,
    string? CancellationReason,
    Guid? SeriesId,
    uint Version,
    LessonProgressDto? Progress);

public sealed record LessonProgressDto(string Note, int? UnderstandingRating);

public sealed record LessonConflict(DateTime RequestedStartUtc, Guid ConflictingLessonId,
    DateTime ConflictingStartUtc, string ConflictsWith);

public sealed record BookingResult(Guid? SeriesId, IReadOnlyList<LessonDto> Lessons);

public sealed record CancelResult(int CancelledCount);
