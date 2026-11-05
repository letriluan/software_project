using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TutorDesk.Api.Features.Lessons;

[ApiController]
[Authorize]
[Route("api/lessons")]
public sealed class LessonsController(LessonService lessons) : ControllerBase
{
    /// <summary>Lessons overlapping [from, to). Max range 62 days.</summary>
    [HttpGet]
    public Task<IReadOnlyList<LessonDto>> List([FromQuery] LessonQuery query, CancellationToken ct) =>
        lessons.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<LessonDto> Get(Guid id, CancellationToken ct) => lessons.GetAsync(id, ct);

    /// <summary>Book one lesson, or a weekly series (RepeatWeekly up to 12). All-or-nothing on conflicts.</summary>
    [HttpPost]
    [ProducesResponseType<BookingResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BookingResult>> Book(BookLessonRequest request, CancellationToken ct)
    {
        var result = await lessons.BookAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Lessons[0].Id }, result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<LessonDto> Reschedule(Guid id, RescheduleLessonRequest request, CancellationToken ct) =>
        lessons.RescheduleAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<CancelResult> Cancel(Guid id, CancelLessonRequest request, CancellationToken ct) =>
        lessons.CancelAsync(id, request, ct);

    [HttpPost("{id:guid}/attendance")]
    public Task<LessonDto> MarkAttendance(Guid id, MarkAttendanceRequest request, CancellationToken ct) =>
        lessons.MarkAttendanceAsync(id, request, ct);
}
