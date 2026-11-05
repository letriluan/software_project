using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using TutorDesk.Api.Auth;
using TutorDesk.Api.Common;
using TutorDesk.Api.Domain;
using TutorDesk.Api.Features.Admin;
using TutorDesk.Api.Features.Students;

namespace TutorDesk.Api.Tests.Unit;

public class AdelaideTimeTests
{
    [Fact]
    public void November_starts_at_local_midnight_in_daylight_time()
    {
        var (start, end) = AdelaideTime.MonthRangeUtc(2026, 11);
        Assert.Equal(new DateTime(2026, 10, 31, 13, 30, 0, DateTimeKind.Utc), start); // +10:30
        Assert.Equal(new DateTime(2026, 11, 30, 13, 30, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void July_uses_standard_time()
    {
        var (start, _) = AdelaideTime.MonthRangeUtc(2026, 7);
        Assert.Equal(new DateTime(2026, 6, 30, 14, 30, 0, DateTimeKind.Utc), start); // +9:30
    }

    [Fact]
    public void Today_follows_adelaide_not_utc()
    {
        // 20:00 UTC on 1 Nov is already 6:30am on 2 Nov in Adelaide.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 2), AdelaideTime.Today(clock));
    }

    [Fact]
    public void Nonexistent_local_time_in_dst_gap_moves_forward()
    {
        // 2:30am on 4 Oct 2026 does not exist in Adelaide (clocks jump 2:00 -> 3:00).
        var utc = AdelaideTime.ToUtc(new DateTime(2026, 10, 4, 2, 30, 0));
        Assert.Equal(3, AdelaideTime.ToLocal(utc).Hour);
    }
}

public class ProgressTrendTests
{
    [Theory]
    [InlineData(new[] { 3, 3, 3 }, "not enough data")]
    [InlineData(new[] { 2, 2, 2, 4, 4, 4 }, "improving")]
    [InlineData(new[] { 5, 5, 4, 3, 3, 3 }, "declining")]
    [InlineData(new[] { 3, 4, 3, 3, 4, 3 }, "steady")]
    public void Trend_compares_last_three_with_previous_three(int[] ratings, string expected) =>
        Assert.Equal(expected, ProgressTrend.From(ratings));
}

public class CsvTests
{
    [Theory]
    [InlineData("Luan Le", "\"Luan Le\"")]
    [InlineData("O\"Brien", "\"O\"\"Brien\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+61 400", "\"'+61 400\"")]
    public void Fields_are_quoted_and_formula_injection_is_neutralised(string input, string expected) =>
        Assert.Equal(expected, ReportService.Csv(input));
}

public class TokenServiceTests
{
    private static TokenService Create() => new(Options.Create(new JwtOptions
    {
        SigningKey = "unit-test-signing-key-0123456789abcdefghij",
        AccessTokenMinutes = 15,
    }));

    [Fact]
    public async Task Access_token_carries_identity_and_role_and_validates()
    {
        var service = Create();
        var before = DateTimeOffset.UtcNow;
        var user = new AppUser { Id = Guid.NewGuid(), Email = "t@test.local", FullName = "Tutor One" };

        var token = service.CreateAccessToken(user, Roles.Tutor);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, service.ValidationParameters());

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(user.Id.ToString(), result.Claims[TokenClaims.Subject]);
        Assert.Equal(Roles.Tutor, result.Claims[TokenClaims.Role]);
        Assert.InRange(token.ExpiresAt, before.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15));
    }

    [Fact]
    public void Refresh_tokens_are_random_and_only_the_hash_is_kept()
    {
        var (raw1, hash1) = TokenService.CreateRefreshToken();
        var (raw2, _) = TokenService.CreateRefreshToken();

        Assert.NotEqual(raw1, raw2);
        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, TokenService.Hash(raw1));
        Assert.DoesNotContain(raw1, hash1, StringComparison.Ordinal);
    }
}
