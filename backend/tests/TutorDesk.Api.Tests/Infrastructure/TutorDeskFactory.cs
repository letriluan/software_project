using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TutorDesk.Api.Features.Admin;
using TutorDesk.Api.Features.Auth;

namespace TutorDesk.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API (same Program.cs, real Postgres in a container, real migrations),
/// with a controllable clock. One instance is shared by every integration test; tests in the
/// collection run sequentially and isolate themselves by creating their own tutors.
/// </summary>
public sealed class TutorDeskFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin!2345";
    public const string Password = "Tutor!2345";

    /// <summary>Monday 2 Nov 2026, 10:30am in Adelaide (UTC+10:30, daylight saving).</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public TestClock Clock { get; } = new(DefaultNow);

    public async Task InitializeAsync() => await _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Jwt:SecureCookies", "false");
        builder.UseSetting("Seed:MigrateOnStartup", "true");
        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("RateLimiting:LoginPerMinute", "10000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient Anonymous(bool cookies = false) =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = cookies, AllowAutoRedirect = false });

    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = password });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    public Task<HttpClient> AdminAsync() => LoginAsync(AdminEmail, AdminPassword);

    /// <summary>Creates a brand-new tutor through the admin API and returns a signed-in client.</summary>
    public async Task<TestTutor> NewTutorAsync(string? name = null)
    {
        var admin = await AdminAsync();
        var email = $"tutor-{Guid.NewGuid():N}@test.local";
        var response = await admin.PostAsJsonAsync("/api/admin/tutors", new CreateTutorRequest
        {
            FullName = name ?? "Test Tutor",
            Email = email,
            TemporaryPassword = Password,
        });
        response.EnsureSuccessStatusCode();
        var summary = await response.Content.ReadFromJsonAsync<TutorSummary>(Json);
        return new TestTutor(summary!.Id, email, await LoginAsync(email, Password));
    }
}

public sealed record TestTutor(Guid Id, string Email, HttpClient Client);

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<TutorDeskFactory>
{
    public const string Name = "api";
}

/// <summary>Base class: resets the clock before each test.</summary>
[Collection(ApiCollection.Name)]
public abstract class IntegrationTest(TutorDeskFactory factory) : IAsyncLifetime
{
    protected TutorDeskFactory Factory { get; } = factory;
    protected TestClock Clock => Factory.Clock;
    protected static JsonSerializerOptions Json => TutorDeskFactory.Json;

    public virtual Task InitializeAsync()
    {
        Clock.SetUtcNow(TutorDeskFactory.DefaultNow);
        return Task.CompletedTask;
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// A settable clock. Unlike FakeTimeProvider it may move backwards, so each test can
/// reset to a known date regardless of what the previous test did.
/// </summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    private readonly Lock _gate = new();

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        lock (_gate) _now = value;
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate) _now += by;
    }
}
