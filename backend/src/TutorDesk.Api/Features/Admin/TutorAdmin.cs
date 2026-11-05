using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorDesk.Api.Common;
using TutorDesk.Api.Data;
using TutorDesk.Api.Domain;
using TutorDesk.Api.Features.Auth;

namespace TutorDesk.Api.Features.Admin;

public sealed record CreateTutorRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string FullName { get; init; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 8)] public string TemporaryPassword { get; init; } = "";
}

public sealed record SetTutorStatusRequest
{
    [Required] public bool? IsActive { get; init; }
}

public sealed record TutorSummary(
    Guid Id,
    string FullName,
    string Email,
    bool IsActive,
    int ActiveStudents,
    int UpcomingLessons,
    double HoursThisMonth,
    DateTime? LastLessonUtc,
    DateTime CreatedAtUtc);

public sealed class TutorAdminService(
    AppDbContext db, UserManager<AppUser> users, AuthService auth, TimeProvider clock)
{
    public async Task<IReadOnlyList<TutorSummary>> ListAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = AdelaideTime.Today(clock);
        var (monthStart, monthEnd) = AdelaideTime.MonthRangeUtc(today.Year, today.Month);
        var tutorIds = (await users.GetUsersInRoleAsync(Roles.Tutor)).Select(u => u.Id).ToList();

        return await db.Users.AsNoTracking()
            .Where(u => tutorIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new TutorSummary(
                u.Id, u.FullName, u.Email ?? "", u.IsActive,
                db.Students.Count(s => s.TutorId == u.Id && !s.IsArchived),
                db.Lessons.Count(l => l.TutorId == u.Id && l.Status == LessonStatus.Scheduled && l.StartsAtUtc > now),
                db.Lessons.Where(l => l.TutorId == u.Id && l.Status == LessonStatus.Completed &&
                                      l.StartsAtUtc >= monthStart && l.StartsAtUtc < monthEnd)
                    .Sum(l => l.DurationMinutes) / 60.0,
                db.Lessons.Where(l => l.TutorId == u.Id && l.Status == LessonStatus.Completed)
                    .Max(l => (DateTime?)l.StartsAtUtc),
                u.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<TutorSummary> CreateAsync(CreateTutorRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email) is not null)
            throw new ConflictException("A user with this email already exists.");

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = r.FullName.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        var created = await users.CreateAsync(user, r.TemporaryPassword);
        if (!created.Succeeded)
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                [nameof(r.TemporaryPassword)] = created.Errors.Select(e => e.Description).ToArray(),
            });
        await users.AddToRoleAsync(user, Roles.Tutor);
        return (await ListAsync(ct)).Single(t => t.Id == user.Id);
    }

    /// <summary>Deactivating blocks login, revokes refresh tokens and (via token validation) live access tokens.</summary>
    public async Task SetStatusAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || !await users.IsInRoleAsync(user, Roles.Tutor))
            throw new NotFoundException("Tutor", id);
        user.IsActive = isActive;
        await users.UpdateAsync(user);
        if (!isActive) await auth.RevokeAllAsync(id, ct);
    }
}

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/tutors")]
public sealed class TutorsAdminController(TutorAdminService tutors) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TutorSummary>> List(CancellationToken ct) => tutors.ListAsync(ct);

    [HttpPost]
    [ProducesResponseType<TutorSummary>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TutorSummary>> Create(CreateTutorRequest request, CancellationToken ct)
    {
        var tutor = await tutors.CreateAsync(request, ct);
        return Created($"/api/admin/tutors/{tutor.Id}", tutor);
    }

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetStatus(Guid id, SetTutorStatusRequest request, CancellationToken ct)
    {
        await tutors.SetStatusAsync(id, request.IsActive!.Value, ct);
        return NoContent();
    }
}
