using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TutorDesk.Api.Auth;
using TutorDesk.Api.Common;
using TutorDesk.Api.Data;
using TutorDesk.Api.Domain;

namespace TutorDesk.Api.Features.Auth;

public sealed record LoginRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(128)] public string Password { get; init; } = "";
}

public sealed record ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; init; } = "";
}

public sealed record UserDto(Guid Id, string Email, string FullName, string Role);

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

/// <summary>Result of a login/refresh: the body for the client plus the refresh token for the cookie.</summary>
public sealed record AuthResult(AuthResponse Response, string RefreshToken, DateTime RefreshExpiresUtc);

public sealed class AuthService(
    AppDbContext db,
    UserManager<AppUser> users,
    TokenService tokens,
    IOptions<JwtOptions> jwt,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    private const string InvalidCredentials = "Email or password is incorrect.";

    public async Task<AuthResult> LoginAsync(LoginRequest request, string? ip, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            logger.LogInformation("Login failed: unknown email");
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (await users.IsLockedOutAsync(user))
        {
            logger.LogWarning("Login blocked: user {UserId} is locked out", user.Id);
            throw new UnauthorizedException("Too many failed attempts. Try again in a few minutes.");
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            logger.LogInformation("Login failed: wrong password for {UserId}", user.Id);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Login blocked: user {UserId} is deactivated", user.Id);
            throw new UnauthorizedException("This account has been deactivated.");
        }

        await users.ResetAccessFailedCountAsync(user);
        return await IssueAsync(user, ip, ct);
    }

    /// <summary>
    /// Rotates the refresh token. Presenting an already-rotated token is treated as theft:
    /// every active token of that user is revoked, forcing a fresh login everywhere.
    /// </summary>
    public async Task<AuthResult> RefreshAsync(string? rawToken, string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw new UnauthorizedException("No refresh token.");

        var now = clock.GetUtcNow().UtcDateTime;
        var hash = TokenService.Hash(rawToken);
        var stored = await db.RefreshTokens.Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored?.User is null) throw new UnauthorizedException("Invalid refresh token.");

        if (stored.RevokedAtUtc is not null)
        {
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions",
                stored.UserId);
            await RevokeAllAsync(stored.UserId, ct);
            throw new UnauthorizedException("Session expired. Please sign in again.");
        }

        if (!stored.IsActive(now) || !stored.User.IsActive)
            throw new UnauthorizedException("Session expired. Please sign in again.");

        var result = await IssueAsync(stored.User, ip, ct, replacing: stored);
        return result;
    }

    public async Task LogoutAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = TokenService.Hash(rawToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedAtUtc: null })
        {
            stored.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);
    }

    public async Task<UserDto> MeAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString())
                   ?? throw new UnauthorizedException("User no longer exists.");
        return await ToDtoAsync(user);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString())
                   ?? throw new UnauthorizedException("User no longer exists.");
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new RequestValidationException(result.Errors
                .GroupBy(e => e.Code.Contains("Mismatch", StringComparison.Ordinal)
                    ? nameof(request.CurrentPassword) : nameof(request.NewPassword))
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
        }
        // Sign out every other session.
        await RevokeAllAsync(user.Id, ct);
    }

    private async Task<AuthResult> IssueAsync(
        AppUser user, string? ip, CancellationToken ct, RefreshToken? replacing = null)
    {
        var dto = await ToDtoAsync(user);
        var access = tokens.CreateAccessToken(user, dto.Role);
        var (raw, hash) = TokenService.CreateRefreshToken();
        var now = clock.GetUtcNow().UtcDateTime;
        var refresh = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = hash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(jwt.Value.RefreshTokenDays),
            CreatedByIp = ip,
        };
        db.RefreshTokens.Add(refresh);
        if (replacing is not null)
        {
            replacing.RevokedAtUtc = now;
            replacing.ReplacedByTokenId = refresh.Id;
        }
        await db.SaveChangesAsync(ct);
        return new AuthResult(new AuthResponse(access.Token, access.ExpiresAt, dto), raw, refresh.ExpiresAtUtc);
    }

    private async Task<UserDto> ToDtoAsync(AppUser user)
    {
        var roles = await users.GetRolesAsync(user);
        var role = roles.Contains(Roles.Admin) ? Roles.Admin : Roles.Tutor;
        return new UserDto(user.Id, user.Email ?? "", user.FullName, role);
    }
}
