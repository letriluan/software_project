using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorDesk.Api.Auth;
using TutorDesk.Api.Common;
using TutorDesk.Api.Data;
using TutorDesk.Api.Domain;
using TutorDesk.Api.Features.Lessons;

namespace TutorDesk.Api.Features.Dashboard;

public sealed record TutorDashboard(
    DateOnly Today,
    IReadOnlyList<LessonDto> TodaysLessons,
    IReadOnlyList<LessonDto> NeedsAttendance,
    int UpcomingThisWeek,
    double HoursThisMonth,
    int CompletedThisMonth,
    int ActiveStudents,
    double? AttendanceRateThisMonth);

[ApiController]
[Authorize(Roles = Roles.Tutor)]
[Route("api/dashboard")]
public sealed class DashboardController(AppDbContext db, ICurrentUser me, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<TutorDashboard> Get(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = AdelaideTime.Today(clock);
        var (dayStart, dayEnd) = AdelaideTime.DayRangeUtc(today);
        var (monthStart, monthEnd) = AdelaideTime.MonthRangeUtc(today.Year, today.Month);
        var weekEnd = AdelaideTime.DayRangeUtc(today.AddDays(7)).StartUtc;
        var mine = db.Lessons.AsNoTracking().Where(l => l.TutorId == me.Id);

        var todays = await mine
            .Where(l => l.StartsAtUtc >= dayStart && l.StartsAtUtc < dayEnd && l.Status != LessonStatus.Cancelled)
            .OrderBy(l => l.StartsAtUtc).Select(LessonService.ToDto).ToListAsync(ct);

        var needsAttendance = await mine
            .Where(l => l.Status == LessonStatus.Scheduled && l.StartsAtUtc <= now &&
                        l.StartsAtUtc >= now.AddDays(-30))
            .OrderBy(l => l.StartsAtUtc).Select(LessonService.ToDto).Take(20).ToListAsync(ct);

        var upcoming = await mine.CountAsync(l =>
            l.Status == LessonStatus.Scheduled && l.StartsAtUtc > now && l.StartsAtUtc < weekEnd, ct);

        var month = await mine
            .Where(l => l.StartsAtUtc >= monthStart && l.StartsAtUtc < monthEnd)
            .Select(l => new { l.Status, l.DurationMinutes }).ToListAsync(ct);
        var completed = month.Where(l => l.Status == LessonStatus.Completed).ToList();
        var noShows = month.Count(l => l.Status == LessonStatus.NoShow);

        var activeStudents = await db.Students.CountAsync(s => s.TutorId == me.Id && !s.IsArchived, ct);

        return new TutorDashboard(
            today, todays, needsAttendance, upcoming,
            Math.Round(completed.Sum(l => l.DurationMinutes) / 60.0, 2),
            completed.Count,
            activeStudents,
            completed.Count + noShows == 0 ? null : Math.Round((double)completed.Count / (completed.Count + noShows), 3));
    }
}
