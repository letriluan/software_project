using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TutorDesk.Api.Common;

/// <summary>Base for errors that map to a specific HTTP status.</summary>
public abstract class AppException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
    public abstract string Title { get; }
}

public sealed class NotFoundException(string resource, object id)
    : AppException($"{resource} '{id}' was not found.")
{
    public override int StatusCode => StatusCodes.Status404NotFound;
    public override string Title => "Not found";
}

public sealed class ConflictException(string message, object? details = null) : AppException(message)
{
    public object? Details { get; } = details;
    public override int StatusCode => StatusCodes.Status409Conflict;
    public override string Title => "Conflict";
}

public sealed class BusinessRuleException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status422UnprocessableEntity;
    public override string Title => "Business rule violated";
}

public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
    public override int StatusCode => StatusCodes.Status400BadRequest;
    public override string Title => "Validation failed";

    public static RequestValidationException For(string field, string error) =>
        new(new Dictionary<string, string[]> { [field] = [error] });
}

public sealed class UnauthorizedException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status401Unauthorized;
    public override string Title => "Unauthorized";
}

/// <summary>Turns every exception into an RFC 9457 problem+json response.</summary>
public sealed class ProblemExceptionHandler(
    IProblemDetailsService problemDetails, ILogger<ProblemExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case RequestValidationException v:
                problem = new ValidationProblemDetails(v.Errors)
                {
                    Status = v.StatusCode, Title = v.Title, Detail = v.Message,
                };
                break;
            case ConflictException c:
                problem = new ProblemDetails { Status = c.StatusCode, Title = c.Title, Detail = c.Message };
                if (c.Details is not null) problem.Extensions["conflicts"] = c.Details;
                break;
            case AppException a:
                problem = new ProblemDetails { Status = a.StatusCode, Title = a.Title, Detail = a.Message };
                break;
            case DbUpdateConcurrencyException:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Concurrency conflict",
                    Detail = "This record was changed by someone else. Reload it and try again.",
                };
                break;
            case DbUpdateException { InnerException: PostgresException pg }
                when pg.SqlState is PostgresErrorCodes.ExclusionViolation
                    or PostgresErrorCodes.UniqueViolation:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflict",
                    Detail = pg.SqlState == PostgresErrorCodes.ExclusionViolation
                        ? "The lesson overlaps another lesson."
                        : "A record with the same value already exists.",
                };
                break;
            case BadHttpRequestException bad:
                problem = new ProblemDetails
                {
                    Status = bad.StatusCode, Title = "Bad request", Detail = bad.Message,
                };
                break;
            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}",
                    http.Request.Method, http.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Something went wrong",
                    Detail = "An unexpected error occurred. Quote the traceId when reporting it.",
                };
                break;
        }

        http.Response.StatusCode = problem.Status ?? 500;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    public static void Customize(ProblemDetailsContext ctx)
    {
        ctx.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
    }
}
