using Microsoft.AspNetCore.Identity;

namespace TutorDesk.Api.Domain;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Tutor = "Tutor";
}

public enum LessonStatus
{
    Scheduled,
    Completed,
    NoShow,
    Cancelled,
}

public enum LessonMode
{
    InPerson,
    Online,
}

public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }
    DateTime UpdatedAtUtc { get; set; }
}

public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}

public class Student : IAuditable
{
    public Guid Id { get; set; }
    public Guid TutorId { get; set; }
    public AppUser? Tutor { get; set; }

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int YearLevel { get; set; }
    public string? School { get; set; }
    public string? ParentName { get; set; }
    public string? ParentEmail { get; set; }
    public string? ParentPhone { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Optimistic concurrency token, mapped to Postgres xmin.</summary>
    public uint Version { get; set; }

    public List<Lesson> Lessons { get; set; } = [];
    public List<ProgressNote> ProgressNotes { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}";
}

public class Subject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Lesson : IAuditable
{
    public Guid Id { get; set; }
    public Guid TutorId { get; set; }
    public AppUser? Tutor { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public DateTime StartsAtUtc { get; set; }
    /// <summary>Stored (not computed) so overlap checks and the exclusion constraint can use it.</summary>
    public DateTime EndsAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public LessonMode Mode { get; set; }
    public string? Location { get; set; }

    public LessonStatus Status { get; set; } = LessonStatus.Scheduled;
    public string? CancellationReason { get; set; }
    public DateTime? AttendanceMarkedAtUtc { get; set; }

    /// <summary>Lessons booked together as a weekly series share a SeriesId.</summary>
    public Guid? SeriesId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public uint Version { get; set; }

    public ProgressNote? ProgressNote { get; set; }
}

public class ProgressNote
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid TutorId { get; set; }
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public string Note { get; set; } = "";
    /// <summary>Tutor's 1-5 rating of how well the student understood the material.</summary>
    public int? UnderstandingRating { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    /// <summary>SHA-256 of the token; the raw token only ever lives in the client's cookie.</summary>
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}
