using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TutorDesk.Api.Domain;

namespace TutorDesk.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required] public string Issuer { get; set; } = "tutordesk";
    [Required] public string Audience { get; set; } = "tutordesk";

    /// <summary>HMAC-SHA256 key, at least 32 characters. Supply via secret / env var Jwt__SigningKey.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = "";

    [Range(1, 1440)] public int AccessTokenMinutes { get; set; } = 15;
    [Range(1, 90)] public int RefreshTokenDays { get; set; } = 14;

    /// <summary>Send the refresh cookie with the Secure flag (disable only for plain-http tests).</summary>
    public bool SecureCookies { get; set; } = true;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

/// <remarks>
/// Token lifetimes use real wall-clock time (as the JWT validator does), independent of the
/// injectable business clock, so tests can move business time freely.
/// </remarks>
public sealed class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _opt = options.Value;

    public IssuedAccessToken CreateAccessToken(AppUser user, string role)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_opt.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _opt.Issuer,
            Audience = _opt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(TokenClaims.Subject, user.Id.ToString()),
                new Claim(TokenClaims.Email, user.Email ?? ""),
                new Claim(TokenClaims.Name, user.FullName),
                new Claim(TokenClaims.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(_opt.Key(), SecurityAlgorithms.HmacSha256),
        };
        return new IssuedAccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    /// <summary>Returns the raw token for the client and its hash for storage.</summary>
    public static (string Raw, string Hash) CreateRefreshToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        return (raw, Hash(raw));
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = _opt.Issuer,
        ValidAudience = _opt.Audience,
        IssuerSigningKey = _opt.Key(),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = TokenClaims.Name,
        RoleClaimType = TokenClaims.Role,
    };
}
