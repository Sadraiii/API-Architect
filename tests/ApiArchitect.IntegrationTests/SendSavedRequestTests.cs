using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiArchitect.Api.Features.Collections;
using ApiArchitect.Api.Features.Environments;
using ApiArchitect.Api.Features.Execution;

namespace ApiArchitect.IntegrationTests;

/// <summary>Sending a saved request: environment variables resolved, sent through the executor, recorded in history.</summary>
[Collection(LoopbackApiCollection.Name)]
public sealed class SendSavedRequestTests(LoopbackApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sends_with_environment_variables_and_records_history()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();
        var environmentId = await CreateEnvironmentAsync(client, projectId);
        var requestId = await CreateSavedRequestAsync(client, projectId, url: "/echo");

        var response = await client.PostAsync($"/api/requests/{requestId}/execute?environmentId={environmentId}", null, Ct);
        var execution = await response.Content.ReadFromJsonAsync<ExecutionResponse>(Ct);
        var echo = JsonDocument.Parse(execution!.ResponseBody).RootElement;
        var history = await client.GetFromJsonAsync<List<ExecutionSummary>>(
            $"/api/projects/{projectId}/executions?requestId={requestId}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, execution.StatusCode);
        Assert.Equal("hello", echo.GetProperty("custom").GetString());
        // The secret token reached the target but is masked everywhere it was stored or echoed.
        Assert.Equal("Bearer " + ExecutionHistory.Mask, echo.GetProperty("authorization").GetString());
        Assert.DoesNotContain("s3cret-token", execution.ResponseBody);
        Assert.Single(history!);
    }

    [Fact]
    public async Task Unknown_variable_returns_validation_problem()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();
        var requestId = await CreateSavedRequestAsync(client, projectId, url: "{{missing}}/echo");

        var response = await client.PostAsync($"/api/requests/{requestId}/execute", null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_users_request_or_environment_is_not_found()
    {
        var (alice, _) = await factory.CreateSignedInClientAsync();
        var (mallory, _) = await factory.CreateSignedInClientAsync();
        var aliceProject = await alice.CreateProjectAsync();
        var aliceRequest = await CreateSavedRequestAsync(alice, aliceProject, url: factory.Target.Url("/echo"));
        var aliceEnvironment = await CreateEnvironmentAsync(alice, aliceProject);
        var malloryProject = await mallory.CreateProjectAsync();
        var malloryRequest = await CreateSavedRequestAsync(mallory, malloryProject, url: factory.Target.Url("/echo"));

        var othersRequest = await mallory.PostAsync($"/api/requests/{aliceRequest}/execute", null, Ct);
        var othersEnvironment = await mallory.PostAsync(
            $"/api/requests/{malloryRequest}/execute?environmentId={aliceEnvironment}", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, othersRequest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, othersEnvironment.StatusCode);
    }

    private async Task<Guid> CreateEnvironmentAsync(HttpClient client, Guid projectId)
    {
        var input = new EnvironmentInput(
            "Local",
            factory.Target.BaseUrl.ToString().TrimEnd('/'),
            [new VariableInput("greeting", "hello"), new VariableInput("token", "s3cret-token", IsSecret: true)],
            null,
            null);
        return await PostForIdAsync(client, $"/api/projects/{projectId}/environments", input);
    }

    private static async Task<Guid> CreateSavedRequestAsync(HttpClient client, Guid projectId, string url)
    {
        var collectionId = await PostForIdAsync(client, $"/api/projects/{projectId}/collections", new CollectionInput("Main", null));
        var input = new SavedRequestInput(
            "Echo", "GET", url, null,
            [new KeyValueDto("X-Custom", "{{greeting}}")], null, null, null,
            new AuthDto(AuthTypes.Bearer, "{{token}}", null, null));
        return await PostForIdAsync(client, $"/api/collections/{collectionId}/requests", input);
    }

    private static async Task<Guid> PostForIdAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return json.GetProperty("id").GetGuid();
    }
}
