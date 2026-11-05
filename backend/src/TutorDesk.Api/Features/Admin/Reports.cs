using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorDesk.Api.Common;
using TutorDesk.Api.Data;
using TutorDesk.Api.Domain;

namespace TutorDesk.Api.Features.Admin;

public sealed class MonthQuery
{
    [Range(2020, 2100)] public int Year { get; init; }
    [Range(1, 12)] public int Month { get; init; }
}

public sealed record ReportTotals(
    double HoursCompleted, int LessonsCompleted, int NoShows, int Cancelled, int StillScheduled,
    int StudentsTaught, double? AttendanceRate);

public sealed record TutorMonthRow(
    Guid TutorId, string TutorName, bool IsActive, double HoursCompleted, int LessonsCompleted,
    int NoShows, int Cancelled, int StillScheduled, int StudentsTaught, double? AttendanceRate);

public sealed record SubjectMonthRow(int SubjectId, string SubjectName, double HoursCompleted, int LessonsCompleted);

public sealed record DailyHours(DateOnly Date, double HoursCompleted);

public sealed record MonthlyReport(
    int Year, int Month, DateTime RangeStartUtc, DateTime RangeEndUtc, ReportTotals Totals,
    IReadOnlyList<TutorMonthRow> Tutors, IReadOnlyList<SubjectMonthRow> Subjects,
    IReadOnlyList<DailyHours> Daily);

public sealed class ReportService(AppDbContext db, UserManager<AppUser> users)
{
    /// <summary>
    /// A lesson belongs to the Adelaide calendar month in which it starts. Hours count only
    /// completed lessons; attendance rate = completed / (completed + no-shows).
    /// </summary>
    public async Task<MonthlyReport> MonthlyAsync(int year, int month, CancellationToken ct)
    {
        var (start, end) = AdelaideTime.MonthRangeUtc(year, month);
        var lessons = await db.Lessons.AsNoTracking()
            .Where(l => l.StartsAtUtc >= start && l.StartsAtUtc < end)
            .Select(l => new
            {
                l.TutorId, l.StudentId, l.SubjectId, SubjectName = l.Subject!.Name,
                l.Status, l.DurationMinutes, l.StartsAtUtc,
            })
            .ToListAsync(ct);

        var tutors = await users.GetUsersInRoleAsync(Roles.Tutor);

        static double Hours(IEnumerable<int> minutes) => Math.Round(minutes.Sum() / 60.0, 2);
        static double? Rate(int completed, int noShows) =>
            completed + noShows == 0 ? null : Math.Round((double)completed / (completed + noShows), 3);

        var tutorRows = tutors
            .Select(t =>
            {
                var mine = lessons.Where(l => l.TutorId == t.Id).ToList();
                var done = mine.Where(l => l.Status == LessonStatus.Completed).ToList();
                var noShows = mine.Count(l => l.Status == LessonStatus.NoShow);
                return new TutorMonthRow(t.Id, t.FullName, t.IsActive,
                    Hours(done.Select(l => l.DurationMinutes)), done.Count, noShows,
                    mine.Count(l => l.Status == LessonStatus.Cancelled),
                    mine.Count(l => l.Status == LessonStatus.Scheduled),
                    done.Select(l => l.StudentId).Distinct().Count(),
                    Rate(done.Count, noShows));
            })
            .OrderByDescending(r => r.HoursCompleted).ThenBy(r => r.TutorName)
            .ToList();

        var completedAll = lessons.Where(l => l.Status == LessonStatus.Completed).ToList();
        var noShowAll = lessons.Count(l => l.Status == LessonStatus.NoShow);

        var subjects = completedAll
            .GroupBy(l => (l.SubjectId, l.SubjectName))
            .Select(g => new SubjectMonthRow(g.Key.SubjectId, g.Key.SubjectName,
                Hours(g.Select(l => l.DurationMinutes)), g.Count()))
            .OrderByDescending(s => s.HoursCompleted)
            .ToList();

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var daily = Enumerable.Range(1, daysInMonth)
            .Select(d => new DateOnly(year, month, d))
            .Select(date => new DailyHours(date, Hours(completedAll
                .Where(l => DateOnly.FromDateTime(AdelaideTime.ToLocal(l.StartsAtUtc)) == date)
                .Select(l => l.DurationMinutes))))
            .ToList();

        var totals = new ReportTotals(
            Hours(completedAll.Select(l => l.DurationMinutes)), completedAll.Count, noShowAll,
            lessons.Count(l => l.Status == LessonStatus.Cancelled),
            lessons.Count(l => l.Status == LessonStatus.Scheduled),
            completedAll.Select(l => l.StudentId).Distinct().Count(),
            Rate(completedAll.Count, noShowAll));

        return new MonthlyReport(year, month, start, end, totals, tutorRows, subjects, daily);
    }

    public static string ToCsv(MonthlyReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("tutor,active,hours_completed,lessons_completed,no_shows,cancelled,still_scheduled,students_taught,attendance_rate");
        foreach (var r in report.Tutors)
        {
            sb.AppendLine(string.Join(',',
                Csv(r.TutorName), r.IsActive ? "yes" : "no",
                r.HoursCompleted.ToString("0.00", CultureInfo.InvariantCulture),
                r.LessonsCompleted, r.NoShows, r.Cancelled, r.StillScheduled, r.StudentsTaught,
                r.AttendanceRate?.ToString("0.000", CultureInfo.InvariantCulture) ?? ""));
        }
        return sb.ToString();
    }

    /// <summary>Quote a CSV field and neutralise spreadsheet formula injection.</summary>
    internal static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal)) value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/reports")]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("monthly")]
    public Task<MonthlyReport> Monthly([FromQuery] MonthQuery q, CancellationToken ct) =>
        reports.MonthlyAsync(q.Year, q.Month, ct);

    [HttpGet("monthly.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> MonthlyCsv([FromQuery] MonthQuery q, CancellationToken ct)
    {
        var report = await reports.MonthlyAsync(q.Year, q.Month, ct);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ReportService.ToCsv(report))).ToArray();
        return File(bytes, "text/csv", $"tutordesk-{q.Year:D4}-{q.Month:D2}.csv");
    }
}
