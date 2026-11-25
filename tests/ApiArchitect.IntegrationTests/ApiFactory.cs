using ApiArchitect.Api.Data;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiArchitect.IntegrationTests;

/// <summary>
/// Runs the real app against a real PostgreSQL database, created fresh for each test run and dropped after.
/// Set <c>TEST_DATABASE</c> to point at a server; it defaults to a local one with the dev credentials.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connectionString =
        (Environment.GetEnvironmentVariable("TEST_DATABASE")
            ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres")
        + $";Database=api_architect_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", _connectionString);
        // Every test signs in from the same address, so the per-IP auth limit would trip.
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "10000");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        ConfigureSettings(builder);
    }

    /// <summary>Lets a derived factory change settings for its own collection of tests.</summary>
    protected virtual void ConfigureSettings(IWebHostBuilder builder)
    {
    }

    public virtual async ValueTask InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>A client with a cookie jar, over HTTPS so the Secure auth cookie is sent back.</summary>
    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    /// <summary>Registers a new, unique user and returns a client signed in as them.</summary>
    public async Task<(HttpClient Client, string Email)> CreateSignedInClientAsync()
    {
        var client = CreateAnonymousClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = TestPasswords.Valid });
        response.EnsureSuccessStatusCode();
        return (client, email);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public static class TestPasswords
{
    public const string Valid = "correct horse battery staple";
}
