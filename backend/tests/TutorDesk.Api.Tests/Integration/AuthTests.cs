using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TutorDesk.Api.Domain;
using TutorDesk.Api.Features.Admin;
using TutorDesk.Api.Features.Auth;
using TutorDesk.Api.Tests.Infrastructure;

namespace TutorDesk.Api.Tests.Integration;

public class AuthTests(TutorDeskFactory factory) : IntegrationTest(factory)
{
    private static LoginRequest Login(string email, string password) => new() { Email = email, Password = password };

    [Fact]
    public async Task Login_returns_access_token_and_sets_httponly_refresh_cookie()
    {
        var client = Factory.Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            Login(TutorDeskFactory.AdminEmail, TutorDeskFactory.AdminPassword));

        var body = await response.ReadAsync<AuthResponse>(HttpStatusCode.OK);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.Equal(Roles.Admin, body.User.Role);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{AuthController.RefreshCookie}=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_give_the_same_401()
    {
        var client = Factory.Anonymous();
        var wrong = await client.PostAsJsonAsync("/api/auth/login", Login(TutorDeskFactory.AdminEmail, "nope-nope"));
        var unknown = await client.PostAsJsonAsync("/api/auth/login", Login("ghost@test.local", "nope-nope"));

        var a = await wrong.ProblemAsync(HttpStatusCode.Unauthorized);
        var b = await unknown.ProblemAsync(HttpStatusCode.Unauthorized);
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Account_locks_after_five_failed_attempts()
    {
        var tutor = await Factory.NewTutorAsync();
        var client = Factory.Anonymous();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", Login(tutor.Email, "wrong-password"));

        var response = await client.PostAsJsonAsync("/api/auth/login", Login(tutor.Email, TutorDeskFactory.Password));
        var problem = await response.ProblemAsync(HttpStatusCode.Unauthorized);
        Assert.Contains("Too many failed attempts", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_fields_return_validation_problem()
    {
        var response = await Factory.Anonymous().PostAsJsonAsync("/api/auth/login", new { email = "not-an-email" });
        var problem = await response.ProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("Password", out _));
    }

    private static string RefreshCookieFrom(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(AuthController.RefreshCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];

    private async Task<HttpResponseMessage> RefreshWith(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return await Factory.Anonymous().SendAsync(request);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_revokes_the_whole_session()
    {
        var tutor = await Factory.NewTutorAsync();
        var login = await Factory.Anonymous().PostAsJsonAsync("/api/auth/login",
            Login(tutor.Email, TutorDeskFactory.Password));
        var first = RefreshCookieFrom(login);

        var rotated = await RefreshWith(first);
        var refreshed = await rotated.ReadAsync<AuthResponse>(HttpStatusCode.OK);
        Assert.Equal(tutor.Id, refreshed.User.Id);
        var second = RefreshCookieFrom(rotated);
        Assert.NotEqual(first, second);

        // Replaying the old token looks like theft...
        await (await RefreshWith(first)).ProblemAsync(HttpStatusCode.Unauthorized);
        // ...so the newer token is revoked too and the user must sign in again.
        await (await RefreshWith(second)).ProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var tutor = await Factory.NewTutorAsync();
        var login = await Factory.Anonymous().PostAsJsonAsync("/api/auth/login",
            Login(tutor.Email, TutorDeskFactory.Password));
        var cookie = RefreshCookieFrom(login);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.NoContent, (await Factory.Anonymous().SendAsync(logout)).StatusCode);

        await (await RefreshWith(cookie)).ProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_without_cookie_is_401()
    {
        await (await Factory.Anonymous().PostAsync("/api/auth/refresh", null))
            .ProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user()
    {
        var tutor = await Factory.NewTutorAsync("Me Tester");
        var me = await (await tutor.Client.GetAsync("/api/auth/me")).ReadAsync<UserDto>(HttpStatusCode.OK);
        Assert.Equal(("Me Tester", Roles.Tutor, tutor.Id), (me.FullName, me.Role, me.Id));
    }

    [Fact]
    public async Task Change_password_requires_the_current_password()
    {
        var tutor = await Factory.NewTutorAsync();
        var bad = await tutor.Client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = "wrong-one", NewPassword = "NewPass!234" });
        var problem = await bad.ProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("CurrentPassword", out _));

        var ok = await tutor.Client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = TutorDeskFactory.Password, NewPassword = "NewPass!234" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        await Factory.LoginAsync(tutor.Email, "NewPass!234");
    }

    [Fact]
    public async Task Deactivated_tutor_is_locked_out_immediately()
    {
        var tutor = await Factory.NewTutorAsync();
        var admin = await Factory.AdminAsync();

        var deactivate = await admin.PutAsJsonAsync($"/api/admin/tutors/{tutor.Id}/status",
            new SetTutorStatusRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        // Existing access token stops working...
        Assert.Equal(HttpStatusCode.Unauthorized, (await tutor.Client.GetAsync("/api/students")).StatusCode);
        // ...and logging in again is refused.
        var login = await Factory.Anonymous().PostAsJsonAsync("/api/auth/login",
            Login(tutor.Email, TutorDeskFactory.Password));
        await login.ProblemAsync(HttpStatusCode.Unauthorized);

        // Reactivating restores access.
        await admin.PutAsJsonAsync($"/api/admin/tutors/{tutor.Id}/status", new SetTutorStatusRequest { IsActive = true });
        await Factory.LoginAsync(tutor.Email, TutorDeskFactory.Password);
    }
}

public class AuthorizationTests(TutorDeskFactory factory) : IntegrationTest(factory)
{
    [Theory]
    [InlineData("/api/students")]
    [InlineData("/api/dashboard")]
    [InlineData("/api/admin/tutors")]
    [InlineData("/api/subjects")]
    public async Task Anonymous_requests_are_rejected(string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.Anonymous().GetAsync(url)).StatusCode);

    [Theory]
    [InlineData("/api/admin/tutors")]
    [InlineData("/api/admin/reports/monthly?year=2026&month=11")]
    [InlineData("/api/admin/reports/monthly.csv?year=2026&month=11")]
    public async Task Tutors_cannot_use_admin_endpoints(string url)
    {
        var tutor = await Factory.NewTutorAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await tutor.Client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Admin_has_no_tutor_dashboard()
    {
        var admin = await Factory.AdminAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var tutor = await Factory.NewTutorAsync();
        var token = tutor.Client.DefaultRequestHeaders.Authorization!.Parameter!;
        var client = Factory.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/students")).StatusCode);
    }
}
