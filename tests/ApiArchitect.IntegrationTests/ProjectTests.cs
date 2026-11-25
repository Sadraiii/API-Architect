using System.Net;
using System.Net.Http.Json;
using ApiArchitect.Api.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace ApiArchitect.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProjectTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ProjectRequest SampleProject = new("Orders API", "Order management", "net11.0");

    [Fact]
    public async Task Listing_projects_requires_sign_in()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/api/projects", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_then_get_returns_the_project()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();

        var created = await client.PostAsJsonAsync("/api/projects", SampleProject, Ct);
        var project = await created.Content.ReadFromJsonAsync<ProjectResponse>(Ct);
        var fetched = await client.GetFromJsonAsync<ProjectResponse>(created.Headers.Location, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Orders API", project!.Name);
        Assert.Equal(project, fetched);
    }

    [Fact]
    public async Task Update_changes_fields_and_UpdatedAt()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateAsync(client, SampleProject);

        var response = await client.PutAsJsonAsync(
            $"/api/projects/{project.Id}", new ProjectRequest("  Billing API  ", "  ", null), Ct);
        var updated = await response.Content.ReadFromJsonAsync<ProjectResponse>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Billing API", updated!.Name);
        Assert.Null(updated.Description);
        Assert.Null(updated.TargetFramework);
        Assert.True(updated.UpdatedAt >= project.UpdatedAt);
    }

    [Fact]
    public async Task Delete_removes_the_project()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateAsync(client, SampleProject);

        var delete = await client.DeleteAsync($"/api/projects/{project.Id}", Ct);
        var get = await client.GetAsync($"/api/projects/{project.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task List_returns_only_your_projects_newest_first()
    {
        var (alice, _) = await factory.CreateSignedInClientAsync();
        var (bob, _) = await factory.CreateSignedInClientAsync();
        var first = await CreateAsync(alice, SampleProject with { Name = "First" });
        var second = await CreateAsync(alice, SampleProject with { Name = "Second" });
        await CreateAsync(bob, SampleProject with { Name = "Bob's" });

        var projects = await alice.GetFromJsonAsync<List<ProjectResponse>>("/api/projects", Ct);

        Assert.Equal([second.Id, first.Id], projects!.Select(p => p.Id));
    }

    [Fact]
    public async Task Another_users_project_is_not_found_for_every_operation()
    {
        var (alice, _) = await factory.CreateSignedInClientAsync();
        var (mallory, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateAsync(alice, SampleProject);
        var url = $"/api/projects/{project.Id}";

        var get = await mallory.GetAsync(url, Ct);
        var put = await mallory.PutAsJsonAsync(url, SampleProject with { Name = "Taken over" }, Ct);
        var delete = await mallory.DeleteAsync(url, Ct);
        var stillThere = await alice.GetFromJsonAsync<ProjectResponse>(url, Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(project.Name, stillThere!.Name);
    }

    [Theory]
    [InlineData("", null, "Name")]
    [InlineData("Valid name", null, "TargetFramework", "net5.0")]
    public async Task Invalid_request_returns_validation_problem(
        string name, string? description, string expectedErrorKey, string? targetFramework = null)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/projects", new ProjectRequest(name, description, targetFramework), Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem!.Errors.Keys, key => key.Equals(expectedErrorKey, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unknown_api_route_returns_404_problem()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync("/api/does-not-exist", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<ProjectResponse> CreateAsync(HttpClient client, ProjectRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/projects", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!;
    }
}
