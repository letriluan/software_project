using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TutorDesk.Api.Auth;

namespace TutorDesk.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth, ICurrentUser currentUser, IOptions<JwtOptions> jwt)
    : ControllerBase
{
    public const string RefreshCookie = "td_refresh";
    private const string CookiePath = "/api/auth";

    /// <summary>Sign in. Returns a short-lived access token; the refresh token is set as an httpOnly cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ClientIp(), ct);
        SetRefreshCookie(result.RefreshToken, result.RefreshExpiresUtc);
        return result.Response;
    }

    /// <summary>Exchange the refresh cookie for a new access token (the cookie is rotated).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        try
        {
            var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie], ClientIp(), ct);
            SetRefreshCookie(result.RefreshToken, result.RefreshExpiresUtc);
            return result.Response;
        }
        catch
        {
            ClearRefreshCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public Task<UserDto> Me() => auth.MeAsync(currentUser.Id);

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(currentUser.Id, request, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private void SetRefreshCookie(string token, DateTime expiresUtc) =>
        Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = jwt.Value.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = expiresUtc,
            IsEssential = true,
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = jwt.Value.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
        });
}

public static class RateLimitPolicies
{
    public const string Login = "login";
}
