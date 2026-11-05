using System.ComponentModel.DataAnnotations;
using TutorDesk.Api.Common;

namespace TutorDesk.Api.Features.Students;

public record StudentUpsertRequest
{
    [Required, StringLength(60)] public string FirstName { get; init; } = "";
    [Required, StringLength(60)] public string LastName { get; init; } = "";
    [Range(1, 12)] public int YearLevel { get; init; }
    [StringLength(120)] public string? School { get; init; }
    [StringLength(120)] public string? ParentName { get; init; }
    [EmailAddress, StringLength(254)] public string? ParentEmail { get; init; }
    [Phone, StringLength(30)] public string? ParentPhone { get; init; }
    [StringLength(2000)] public string? Notes { get; init; }
}

public sealed record UpdateStudentRequest : StudentUpsertRequest
{
    /// <summary>The version the client last read; a stale value returns 409.</summary>
    [Required] public uint? Version { get; init; }
}

public sealed class StudentQuery : PageQuery
{
    [StringLength(60)] public string? Search { get; init; }
    public bool IncludeArchived { get; init; }
    /// <summary>Admin only: filter by tutor.</summary>
    public Guid? TutorId { get; init; }
}

public sealed record StudentListItem(
    Guid Id,
    string FirstName,
    string LastName,
    int YearLevel,
    string? School,
    bool IsArchived,
    Guid TutorId,
    string TutorName,
    DateTime? NextLessonUtc,
    int CompletedLessons);

public sealed record StudentDetail(
    Guid Id,
    string FirstName,
    string LastName,
    int YearLevel,
    string? School,
    string? ParentName,
    string? ParentEmail,
    string? ParentPhone,
    string? Notes,
    bool IsArchived,
    Guid TutorId,
    string TutorName,
    DateTime CreatedAtUtc,
    uint Version,
    StudentStats Stats);

public sealed record StudentStats(
    int CompletedLessons,
    int NoShows,
    int CancelledLessons,
    int UpcomingLessons,
    double HoursTaught,
    double? AttendanceRate);

public sealed record ProgressNoteDto(
    Guid Id,
    Guid? LessonId,
    int SubjectId,
    string SubjectName,
    string Note,
    int? UnderstandingRating,
    DateTime CreatedAtUtc,
    DateTime? LessonStartsAtUtc);

public sealed record SubjectProgressSummary(
    int SubjectId,
    string SubjectName,
    int Notes,
    double? AverageRating,
    int? LatestRating,
    string Trend);

public sealed record StudentProgress(
    IReadOnlyList<SubjectProgressSummary> Subjects,
    IReadOnlyList<ProgressNoteDto> Notes);

public sealed record ArchiveResult(int CancelledFutureLessons);
