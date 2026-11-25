using System.Net;
using System.Net.Http.Json;
using ApiArchitect.Api.Features.Execution;
using Microsoft.AspNetCore.Mvc;
using static ApiArchitect.IntegrationTests.ExecutionTestHelpers;

namespace ApiArchitect.IntegrationTests;

/// <summary>Executor behaviour in strict mode (AllowPrivateNetworks off): nothing internal is reachable.</summary>
[Collection(ApiCollection.Name)]
public sealed class ExecutionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost:5432/")] // a hostname, so the block happens after DNS, at connect time
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://2130706433/")] // 127.0.0.1 written as one decimal number
    [InlineData("http://0x7f.1/")] // and in hex shorthand
    public async Task Internal_targets_are_blocked_and_recorded(string url)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var execution = await client.ExecuteAsync(projectId, Get(url));
        var history = await client.GetFromJsonAsync<List<ExecutionSummary>>($"/api/projects/{projectId}/executions", Ct);

        Assert.Null(execution.StatusCode);
        Assert.Equal(ExecutionErrorCodes.BlockedAddress, execution.Error?.Code);
        Assert.Equal(execution.Id, Assert.Single(history!).Id);
    }

    [Theory]
    [InlineData("TRACE", "https://example.com/", null, "method")]
    [InlineData("GET", "ftp://example.com/", null, "url")]
    [InlineData("GET", "file:///etc/passwd", null, "url")]
    [InlineData("GET", "not a url", null, "url")]
    [InlineData("GET", "https://user:pass@example.com/", null, "url")]
    [InlineData("GET", "https://example.com/", "Host", "headers")]
    [InlineData("GET", "https://example.com/", "Bad Name", "headers")]
    public async Task Invalid_requests_return_validation_problem(string method, string url, string? headerName, string expectedKey)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();
        List<HeaderPair>? headers = headerName is null ? null : [new HeaderPair(headerName, "x")];

        var response = await client.PostExecuteAsync(projectId, new ExecuteRequest(method, url, headers, null, null, null));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem!.Errors.Keys, key => key.Equals(expectedKey, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Header_values_with_line_breaks_are_rejected()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var response = await client.PostExecuteAsync(projectId,
            Get("https://example.com/") with { Headers = [new HeaderPair("X-Test", "a\r\nInjected: yes")] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_key_auth_needs_a_name()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var response = await client.PostExecuteAsync(projectId,
            Get("https://example.com/") with { Auth = new RequestAuth(AuthKind.ApiKeyHeader, "secret") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Execution_requires_sign_in()
    {
        var response = await factory.CreateAnonymousClient()
            .PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/execute", Get("https://example.com/"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Another_users_project_and_history_are_not_found_for_every_operation()
    {
        var (alice, _) = await factory.CreateSignedInClientAsync();
        var (mallory, _) = await factory.CreateSignedInClientAsync();
        var projectId = await alice.CreateProjectAsync();
        var execution = await alice.ExecuteAsync(projectId, Get("http://127.0.0.1/"));

        var execute = await mallory.PostExecuteAsync(projectId, Get("http://127.0.0.1/"));
        var list = await mallory.GetAsync($"/api/projects/{projectId}/executions", Ct);
        var get = await mallory.GetAsync($"/api/executions/{execution.Id}", Ct);
        var clear = await mallory.DeleteAsync($"/api/projects/{projectId}/executions", Ct);
        var stillThere = await alice.GetAsync($"/api/executions/{execution.Id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, execute.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, clear.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task Clearing_history_removes_every_execution()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();
        await client.ExecuteAsync(projectId, Get("http://127.0.0.1/"));

        var clear = await client.DeleteAsync($"/api/projects/{projectId}/executions", Ct);
        var history = await client.GetFromJsonAsync<List<ExecutionSummary>>($"/api/projects/{projectId}/executions", Ct);

        Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
        Assert.Empty(history!);
    }
}
