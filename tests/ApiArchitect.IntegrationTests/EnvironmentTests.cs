using System.Net;
using System.Net.Http.Json;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Features.Collections;
using ApiArchitect.Api.Features.Environments;
using ApiArchitect.Api.Features.Projects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiArchitect.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class EnvironmentTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EnvironmentInput Staging(string? secretValue = "s3cret-token", string? authValue = "api-key-123") => new(
        "Staging",
        "https://staging.example.com",
        [new VariableInput("tenant", "acme"), new VariableInput("token", secretValue, IsSecret: true)],
        [new KeyValueDto("X-Tenant", "{{tenant}}")],
        new EnvironmentAuthInput(AuthTypes.ApiKey, authValue, "X-Api-Key", ApiKeyLocations.Header));

    [Fact]
    public async Task Secrets_are_never_returned()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await CreateProjectAsync(client);

        var created = await client.PostAsJsonAsync($"/api/projects/{projectId}/environments", Staging(), Ct);
        var raw = await created.Content.ReadAsStringAsync(Ct);
        var environment = await created.Content.ReadFromJsonAsync<EnvironmentResponse>(Ct);
        var listed = await client.GetStringAsync($"/api/projects/{projectId}/environments", Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.DoesNotContain("s3cret-token", raw);
        Assert.DoesNotContain("api-key-123", raw);
        Assert.DoesNotContain("s3cret-token", listed);
        Assert.Equal(
            [new VariableResponse("tenant", "acme", false), new VariableResponse("token", null, true)],
            environment!.Variables);
        Assert.Equal(new EnvironmentAuthResponse(AuthTypes.ApiKey, "X-Api-Key", ApiKeyLocations.Header, HasValue: true), environment.Auth);
    }

    [Fact]
    public async Task Secrets_are_encrypted_in_the_database()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await CreateProjectAsync(client);
        var environment = await CreateAsync(client, projectId, Staging());

        var stored = await LoadAsync(environment.Id);

        Assert.Equal("acme", stored.Variables.Single(v => v.Name == "tenant").Value);
        Assert.DoesNotContain("s3cret-token", stored.Variables.Single(v => v.Name == "token").Value);
        Assert.DoesNotContain("api-key-123", stored.Auth.ProtectedValue);
    }

    [Fact]
    public async Task Saving_with_null_secrets_keeps_the_stored_values()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await CreateProjectAsync(client);
        var environment = await CreateAsync(client, projectId, Staging());

        var update = await client.PutAsJsonAsync(
            $"/api/environments/{environment.Id}", Staging(secretValue: null, authValue: null) with { Name = "Staging EU" }, Ct);
        var resolved = await ResolveAsync(environment.Id, new SavedRequest
        {
            Name = "x",
            Method = "GET",
            Url = "/orders?token={{token}}",
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("https://staging.example.com/orders?token=s3cret-token", resolved.Url.AbsoluteUri);
        Assert.Equal(new ResolvedAuth(ResolvedAuthKind.ApiKeyHeader, "api-key-123", "X-Api-Key"), resolved.Auth);
        Assert.Equal([new("X-Tenant", "acme")], resolved.Headers);
    }

    [Fact]
    public async Task Changing_a_secret_replaces_it()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await CreateProjectAsync(client);
        var environment = await CreateAsync(client, projectId, Staging());

        await client.PutAsJsonAsync($"/api/environments/{environment.Id}", Staging(secretValue: "rotated"), Ct);
        var resolved = await ResolveAsync(environment.Id, new SavedRequest { Name = "x", Method = "GET", Url = "/{{token}}" });

        Assert.Equal("https://staging.example.com/rotated", resolved.Url.AbsoluteUri);
    }

    [Theory]
    [InlineData("new secret without a value", "Variables[1].Value")]
    [InlineData("duplicate variable", "Variables[1].Name")]
    [InlineData("bad variable name", "Variables[0].Name")]
    [InlineData("relative base URL", "BaseUrl")]
    [InlineData("auth without a value", "Auth.Value")]
    public async Task Invalid_environment_returns_validation_problem(string scenario, string expectedKey)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await CreateProjectAsync(client);
        var input = scenario switch
        {
            "new secret without a value" => Staging(secretValue: null),
            "duplicate variable" => Staging() with { Variables = [new VariableInput("a", "1"), new VariableInput("a", "2")] },
            "bad variable name" => Staging() with { Variables = [new VariableInput("has space", "1")] },
            "relative base URL" => Staging() with { BaseUrl = "/api" },
            _ => Staging(authValue: null),
        };

        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/environments", input, Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem!.Errors.Keys, key => key.Equals(expectedKey, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Another_users_environments_are_not_found_for_every_operation()
    {
        var (alice, _) = await factory.CreateSignedInClientAsync();
        var (mallory, _) = await factory.CreateSignedInClientAsync();
        var projectId = await CreateProjectAsync(alice);
        var environment = await CreateAsync(alice, projectId, Staging());
        var url = $"/api/environments/{environment.Id}";

        HttpResponseMessage[] responses =
        [
            await mallory.GetAsync($"/api/projects/{projectId}/environments", Ct),
            await mallory.PostAsJsonAsync($"/api/projects/{projectId}/environments", Staging(), Ct),
            await mallory.GetAsync(url, Ct),
            await mallory.PutAsJsonAsync(url, Staging() with { Name = "Taken" }, Ct),
            await mallory.DeleteAsync(url, Ct),
        ];
        var stillThere = await alice.GetFromJsonAsync<EnvironmentResponse>(url, Ct);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal("Staging", stillThere!.Name);
    }

    [Fact]
    public async Task Delete_removes_the_environment()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await CreateProjectAsync(client);
        var environment = await CreateAsync(client, projectId, Staging());

        var delete = await client.DeleteAsync($"/api/environments/{environment.Id}", Ct);
        var list = await client.GetFromJsonAsync<List<EnvironmentResponse>>($"/api/projects/{projectId}/environments", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(list!);
    }

    private async Task<ApiEnvironment> LoadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Environments.AsNoTracking().SingleAsync(e => e.Id == id, Ct);
    }

    /// <summary>Resolves a request against the stored environment, which proves the secrets still decrypt.</summary>
    private async Task<ResolvedRequest> ResolveAsync(Guid environmentId, SavedRequest request)
    {
        var environment = await LoadAsync(environmentId);
        var result = factory.Services.GetRequiredService<RequestResolver>().Resolve(request, environment);
        Assert.Empty(result.Errors);
        return result.Request!;
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new ProjectRequest("Orders API", null, null), Ct);
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!.Id;
    }

    private static async Task<EnvironmentResponse> CreateAsync(HttpClient client, Guid projectId, EnvironmentInput input)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/environments", input, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EnvironmentResponse>(Ct))!;
    }
}
