using TutorDesk.Api.Common;
using TutorDesk.Api.Domain;

namespace TutorDesk.Api.Auth;

public interface ICurrentUser
{
    Guid Id { get; }
    bool IsAdmin { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid Id
    {
        get
        {
            var sub = accessor.HttpContext?.User.FindFirst(TokenClaims.Subject)?.Value;
            return Guid.TryParse(sub, out var id)
                ? id
                : throw new UnauthorizedException("No authenticated user.");
        }
    }

    public bool IsAdmin => accessor.HttpContext?.User.IsInRole(Roles.Admin) ?? false;
}

public static class TokenClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
}
