using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorDesk.Api.Data;

namespace TutorDesk.Api.Features.Subjects;

public sealed record SubjectDto(int Id, string Name);

[ApiController]
[Authorize]
[Route("api/subjects")]
public sealed class SubjectsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<SubjectDto>> List(CancellationToken ct) =>
        await db.Subjects.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new SubjectDto(s.Id, s.Name)).ToListAsync(ct);
}
