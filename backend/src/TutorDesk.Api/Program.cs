using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TutorDesk.Api.Auth;
using TutorDesk.Api.Common;
using TutorDesk.Api.Data;
using TutorDesk.Api.Domain;
using TutorDesk.Api.Features.Admin;
using TutorDesk.Api.Features.Auth;
using TutorDesk.Api.Features.Lessons;
using TutorDesk.Api.Features.Students;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- configuration ----------
builder.Services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.Configure<SeedOptions>(config.GetSection(SeedOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);

// ---------- data ----------
var connectionString = config.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString,
    npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

// ---------- auth ----------
builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TokenService>((o, tokens) =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = tokens.ValidationParameters();
        o.Events = new JwtBearerEvents
        {
            // Deactivated tutors lose access immediately, not when their token expires.
            OnTokenValidated = async ctx =>
            {
                var sub = ctx.Principal?.FindFirst(TokenClaims.Subject)?.Value;
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var active = Guid.TryParse(sub, out var id) &&
                             await db.Users.AnyAsync(u => u.Id == id && u.IsActive);
                if (!active) ctx.Fail("User is inactive.");
            },
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ---------- application services ----------
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<StudentService>();
builder.Services.AddScoped<LessonService>();
builder.Services.AddScoped<TutorAdminService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<DbInitializer>();

// ---------- web ----------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemExceptionHandler.Customize);
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Login, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimiting:LoginPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Azure Container Apps / App Service terminate TLS in front of the app.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---------- observability ----------
var otel = builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(Telemetry.Name))
    .WithTracing(t => t.AddSource(Telemetry.Name));
if (!string.IsNullOrWhiteSpace(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    otel.UseAzureMonitor();

var app = builder.Build();

// ---------- pipeline ----------
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (!ctx.Request.Path.StartsWithSegments("/swagger"))
    {
        h.ContentSecurityPolicy =
            "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
            "connect-src 'self' https://*.applicationinsights.azure.com; frame-ancestors 'none'";
    }
    await next();
});

if (app.Environment.IsDevelopment() || config.GetValue("Api:ExposeDocs", false))
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "TutorDesk API v1"));
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// Unknown /api routes are real 404s; everything else falls through to the React app.
app.Map("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "Not found"));
app.MapFallbackToFile("index.html");

// ---------- database ----------
using (var scope = app.Services.CreateScope())
{
    var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
    await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync(seed);
}

await app.RunAsync();

public partial class Program;
