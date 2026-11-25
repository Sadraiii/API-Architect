using System.Net.Http.Json;
using ApiArchitect.Api.Features.Execution;
using ApiArchitect.Api.Features.Projects;

namespace ApiArchitect.IntegrationTests;

internal static class ExecutionTestHelpers
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Guid> CreateProjectAsync(this HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new ProjectRequest("Target API", null, null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!.Id;
    }

    public static Task<HttpResponseMessage> PostExecuteAsync(this HttpClient client, Guid projectId, ExecuteRequest request) =>
        client.PostAsJsonAsync($"/api/projects/{projectId}/execute", request, Ct);

    public static async Task<ExecutionResponse> ExecuteAsync(this HttpClient client, Guid projectId, ExecuteRequest request)
    {
        var response = await client.PostExecuteAsync(projectId, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExecutionResponse>(Ct))!;
    }

    public static ExecuteRequest Get(string url) => new("GET", url, null, null, null, null);
}
