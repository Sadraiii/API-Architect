using System.Security.Claims;
using System.Threading.RateLimiting;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Features.ClientCode;
using ApiArchitect.Api.Features.Auth;
using ApiArchitect.Api.Features.Collections;
using ApiArchitect.Api.Features.Designs;
using ApiArchitect.Api.Features.Environments;
using ApiArchitect.Api.Features.Execution;
using ApiArchitect.Api.Features.Projects;
using ApiArchitect.Api.Features.Specifications;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")
        ?? throw new InvalidOperationException("Connection string 'Database' is not configured.")));

builder.Services.AddSingleton(TimeProvider.System);

// Data Protection encrypts the auth cookie and environment secrets. The application name keeps the key ring
// shared across instances and deployments; production must also persist the keys (see README).
builder.Services.AddDataProtection().SetApplicationName("ApiArchitect");
builder.Services.AddSingleton<EnvironmentSecrets>();
builder.Services.AddSingleton<RequestResolver>();

// Authentication: ASP.NET Core Identity with its application cookie only (no bearer tokens).
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // Length over composition rules, in line with NIST SP 800-63B.
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "aa_auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;

    // This is an API: answer with status codes instead of redirecting to a login page.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

var rateLimits = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
    ?? new RateLimitingOptions();
var executionOptions = builder.Configuration.GetSection(ExecutionOptions.SectionName).Get<ExecutionOptions>()
    ?? new ExecutionOptions();
builder.Services.Configure<ExecutionOptions>(builder.Configuration.GetSection(ExecutionOptions.SectionName));
builder.Services.AddSingleton<RequestExecutor>();
builder.Services.AddScoped<ExecutionHistory>();
builder.Services.AddSingleton<SpecificationFetcher>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimits.AuthPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
            }));
    // Per user: every execution makes this server call out, so one account must not turn it into a scanner.
    options.AddPolicy(ExecutionEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = executionOptions.PermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
            }));
});

// Errors use RFC 9457 ProblemDetails everywhere: validation, 404s, 401s and unhandled exceptions.
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}
else
{
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseSecurityHeaders();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    // This app's own API reference: /openapi/v1.json and /scalar.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapProjectEndpoints();
app.MapCollectionEndpoints();
app.MapSavedRequestEndpoints();
app.MapSendSavedRequestEndpoint();
app.MapEnvironmentEndpoints();
app.MapExecutionEndpoints();
app.MapSpecificationEndpoints();
app.MapClientCodeEndpoints();
app.MapDesignEndpoints();

// Unknown /api routes are real 404s; every other unknown path belongs to the SPA's client-side router.
app.MapFallback("/api/{**path}", () => TypedResults.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

// Lets the integration tests use WebApplicationFactory<Program>.
public partial class Program;
