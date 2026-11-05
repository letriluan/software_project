using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TutorDesk.Api.Common;

namespace TutorDesk.Api.Features.Students;

[ApiController]
[Authorize]
[Route("api/students")]
public sealed class StudentsController(StudentService students) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<StudentListItem>> List([FromQuery] StudentQuery query, CancellationToken ct) =>
        students.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StudentDetail> Get(Guid id, CancellationToken ct) => students.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<StudentDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StudentDetail>> Create(StudentUpsertRequest request, CancellationToken ct)
    {
        var created = await students.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<StudentDetail> Update(Guid id, UpdateStudentRequest request, CancellationToken ct) =>
        students.UpdateAsync(id, request, ct);

    /// <summary>Archive (soft delete). Future scheduled lessons are cancelled.</summary>
    [HttpDelete("{id:guid}")]
    public Task<ArchiveResult> Archive(Guid id, CancellationToken ct) => students.ArchiveAsync(id, ct);

    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await students.RestoreAsync(id, ct);
        return NoContent();
    }

    /// <summary>Progress notes timeline plus a per-subject summary and trend.</summary>
    [HttpGet("{id:guid}/progress")]
    public Task<StudentProgress> Progress(Guid id, [FromQuery] int? subjectId, CancellationToken ct) =>
        students.ProgressAsync(id, subjectId, ct);
}
