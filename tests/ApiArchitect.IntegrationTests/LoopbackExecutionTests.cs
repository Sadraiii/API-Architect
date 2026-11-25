using System.Net.Http.Json;
using System.Text.Json;
using ApiArchitect.Api.Features.Execution;
using static ApiArchitect.IntegrationTests.ExecutionTestHelpers;

namespace ApiArchitect.IntegrationTests;

/// <summary>Executor behaviour against a real HTTP server, with loopback allowed for this collection only.</summary>
[Collection(LoopbackApiCollection.Name)]
public sealed class LoopbackExecutionTests(LoopbackApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Url(string path) => factory.Target.Url(path);

    [Fact]
    public async Task Sends_method_headers_body_and_bearer_token_and_returns_the_response()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var execution = await client.ExecuteAsync(projectId, new ExecuteRequest(
            "POST",
            Url("/echo?page=2"),
            [new HeaderPair("X-Custom", "hello")],
            """{"name":"Ada"}""",
            "application/json",
            new RequestAuth(AuthKind.Bearer, "token-123")));
        var echo = JsonDocument.Parse(execution.ResponseBody).RootElement;

        Assert.Equal(200, execution.StatusCode);
        Assert.Null(execution.Error);
        Assert.Equal("POST", echo.GetProperty("method").GetString());
        Assert.Equal("?page=2", echo.GetProperty("query").GetString());
        Assert.StartsWith("application/json", echo.GetProperty("contentType").GetString());
        // The target got the token; the echo of it is masked because it is a secret.
        Assert.Equal("Bearer " + ExecutionHistory.Mask, echo.GetProperty("authorization").GetString());
        Assert.Equal("hello", echo.GetProperty("custom").GetString());
        Assert.Equal("ApiArchitect/1.0", echo.GetProperty("userAgent").GetString());
        Assert.Equal("""{"name":"Ada"}""", echo.GetProperty("body").GetString());
        Assert.Contains(execution.ResponseHeaders, h => h.Name == "Content-Type" && h.Value.StartsWith("application/json"));
        Assert.True(execution.DurationMs >= 0);
    }

    [Fact]
    public async Task Api_key_in_query_is_sent_but_never_stored()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var execution = await client.ExecuteAsync(projectId,
            Get(Url("/echo")) with { Auth = new RequestAuth(AuthKind.ApiKeyQuery, "k3y/val", "api_key") });
        var stored = await client.GetFromJsonAsync<ExecutionResponse>($"/api/executions/{execution.Id}", Ct);

        // The target received the key (it echoes the query string), and it is masked even in the live response.
        Assert.Contains("api_key=" + ExecutionHistory.Mask, execution.ResponseBody);
        Assert.DoesNotContain("k3y", execution.ResponseBody);
        Assert.DoesNotContain("k3y", stored!.ResponseBody);
        Assert.DoesNotContain("k3y", stored.Url);
        Assert.Equal(new AuthSummary(AuthKind.ApiKeyQuery, "api_key"), stored.Auth);
    }

    [Fact]
    public async Task Secrets_are_masked_in_stored_headers_and_echoed_bodies()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var execution = await client.ExecuteAsync(projectId, Get(Url("/echo")) with
        {
            Headers = [new HeaderPair("X-Api-Key", "header-secret"), new HeaderPair("Cookie", "session=abc")],
            Auth = new RequestAuth(AuthKind.Bearer, "bearer-secret"),
        });
        var stored = await client.GetFromJsonAsync<ExecutionResponse>($"/api/executions/{execution.Id}", Ct);
        var json = JsonSerializer.Serialize(stored);

        Assert.DoesNotContain("bearer-secret", json);
        Assert.DoesNotContain("header-secret", stored!.RequestHeaders.Single(h => h.Name == "X-Api-Key").Value);
        Assert.Equal(ExecutionHistory.Mask, stored.RequestHeaders.Single(h => h.Name == "Cookie").Value);
    }

    [Fact]
    public async Task Redirects_are_returned_not_followed()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var execution = await client.ExecuteAsync(projectId, Get(Url("/redirect")));

        Assert.Equal(302, execution.StatusCode);
        Assert.Contains(execution.ResponseHeaders, h => h.Name == "Location" && h.Value.Contains("169.254.169.254"));
    }

    [Fact]
    public async Task Large_responses_are_truncated_at_the_limit()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var execution = await client.ExecuteAsync(projectId, Get(Url("/large")));
        var stored = await client.GetFromJsonAsync<ExecutionResponse>($"/api/executions/{execution.Id}", Ct);

        Assert.Equal(200, execution.StatusCode);
        Assert.True(execution.ResponseBodyTruncated);
        Assert.Equal(LoopbackApiFactory.MaxResponseBytes, execution.ResponseBody.Length);
        Assert.Equal(LoopbackApiFactory.MaxResponseBytes, execution.ResponseSizeBytes);
        Assert.Equal(1024, stored!.ResponseBody.Length); // history keeps a smaller copy
        Assert.True(stored.ResponseBodyTruncated);
    }

    [Fact]
    public async Task Slow_targets_time_out()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var execution = await client.ExecuteAsync(projectId, Get(Url("/slow")));

        Assert.Null(execution.StatusCode);
        Assert.Equal(ExecutionErrorCodes.Timeout, execution.Error?.Code);
    }

    [Fact]
    public async Task Binary_bodies_are_flagged_not_decoded()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var execution = await client.ExecuteAsync(projectId, Get(Url("/binary")));

        Assert.True(execution.ResponseBodyIsBinary);
        Assert.Equal("", execution.ResponseBody);
        Assert.Equal(6, execution.ResponseSizeBytes);
    }

    [Fact]
    public async Task Text_is_decoded_with_the_declared_charset()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var execution = await client.ExecuteAsync(projectId, Get(Url("/latin1")));

        Assert.Equal("café", execution.ResponseBody);
    }

    [Fact]
    public async Task Refused_connections_report_connection_failed()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        // Port 1 on loopback: allowed by this factory's policy, but nothing listens there.
        var execution = await client.ExecuteAsync(projectId, Get("http://127.0.0.1:1/"));

        Assert.Equal(ExecutionErrorCodes.ConnectionFailed, execution.Error?.Code);
    }

    [Fact]
    public async Task History_keeps_only_the_newest_executions_per_project()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < LoopbackApiFactory.HistoryLimit + 2; i++)
        {
            ids.Add((await client.ExecuteAsync(projectId, Get(Url($"/echo?n={i}")))).Id);
        }

        var history = await client.GetFromJsonAsync<List<ExecutionSummary>>($"/api/projects/{projectId}/executions", Ct);

        Assert.Equal(ids.TakeLast(LoopbackApiFactory.HistoryLimit).Reverse(), history!.Select(e => e.Id));
    }
}
