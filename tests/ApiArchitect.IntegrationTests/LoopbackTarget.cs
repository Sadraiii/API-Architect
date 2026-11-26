using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ApiArchitect.IntegrationTests;

/// <summary>
/// A real HTTP server on 127.0.0.1 for the executor to call.
/// </summary>
public sealed class LoopbackTarget : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LoopbackTarget(WebApplication app, Uri baseUrl, ConcurrentDictionary<string, string> documents)
    {
        _app = app;
        BaseUrl = baseUrl;
        Documents = documents;
    }

    public Uri BaseUrl { get; }

    /// <summary>Served as JSON at <c>/documents/{key}</c>. Tests change an entry to simulate a new API version.</summary>
    public ConcurrentDictionary<string, string> Documents { get; }

    public string Url(string path) => new Uri(BaseUrl, path).ToString();

    public static async Task<LoopbackTarget> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var documents = new ConcurrentDictionary<string, string>();

        // Echoes what arrived, so tests can check what the executor actually sent.
        app.MapMethods("/echo", ["GET", "POST", "PUT", "PATCH", "DELETE"], async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            return Results.Json(new
            {
                method = request.Method,
                query = request.QueryString.Value,
                contentType = request.ContentType,
                authorization = request.Headers.Authorization.ToString(),
                apiKey = request.Headers["X-Api-Key"].ToString(),
                custom = request.Headers["X-Custom"].ToString(),
                userAgent = request.Headers.UserAgent.ToString(),
                body = await reader.ReadToEndAsync(),
            });
        });
        app.MapGet("/documents/{key}", (string key) =>
            documents.TryGetValue(key, out var document) ? Results.Text(document, "application/json") : Results.NotFound());
        app.MapGet("/redirect", () => Results.Redirect("http://169.254.169.254/latest/meta-data/"));
        app.MapGet("/large", () => Results.Text(new string('a', 10_000), "text/plain"));
        app.MapGet("/slow", async (CancellationToken ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return Results.Ok();
        });
        app.MapGet("/binary", () => Results.Bytes([0x89, 0x50, 0x4E, 0x47, 0x00, 0x01], "image/png"));
        app.MapGet("/latin1", () => Results.Bytes(Encoding.Latin1.GetBytes("café"), "text/plain; charset=iso-8859-1"));

        await app.StartAsync();
        return new LoopbackTarget(app, new Uri(app.Urls.Single()), documents);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

/// <summary>
/// The app as configured for local use (private networks allowed), with small limits so tests can hit them, and a
/// test server to call. Its own collection and database, so its settings never reach other tests.
/// </summary>
public sealed class LoopbackApiFactory : ApiFactory
{
    public const int MaxResponseBytes = 4096;
    public const int HistoryLimit = 3;

    public LoopbackTarget Target { get; private set; } = null!;

    protected override void ConfigureSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Execution:AllowPrivateNetworks", "true");
        builder.UseSetting("Execution:MaxResponseBytes", MaxResponseBytes.ToString());
        builder.UseSetting("Execution:MaxStoredBodyBytes", "1024");
        builder.UseSetting("Execution:TimeoutSeconds", "2");
        builder.UseSetting("Execution:HistoryLimitPerProject", HistoryLimit.ToString());
    }

    public override async ValueTask InitializeAsync()
    {
        Target = await LoopbackTarget.StartAsync();
        await base.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await Target.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class LoopbackApiCollection : ICollectionFixture<LoopbackApiFactory>
{
    public const string Name = "loopback-api";
}
