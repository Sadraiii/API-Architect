using System.Net;
using System.Net.Http.Json;
using ApiArchitect.Api.Features.Projects;
using ApiArchitect.Api.Features.Specifications;
using Microsoft.AspNetCore.Mvc;

namespace ApiArchitect.IntegrationTests;

/// <summary>Importing from a URL and re-importing, against a real local server.</summary>
[Collection(LoopbackApiCollection.Name)]
public sealed class SpecificationUrlImportTests(LoopbackApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Serve(string document)
    {
        var key = Guid.NewGuid().ToString("N");
        factory.Target.Documents[key] = document;
        return factory.Target.Url($"/documents/{key}");
    }

    [Fact]
    public async Task Import_from_url_into_a_project_keeps_the_url_and_defaults_the_name_to_the_title()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();
        var url = Serve(SpecificationTests.PetsDocument);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/specifications/from-url", new SpecificationUrlRequest(url, null), Ct);
        var specification = await response.Content.ReadFromJsonAsync<SpecificationResponse>(Ct);
        var exported = await client.GetStringAsync($"/api/specifications/{specification!.Id}/document", Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("Pets API", url), (specification.Name, specification.SourceUrl));
        Assert.Equal(SpecificationTests.PetsDocument, exported);
    }

    [Fact]
    public async Task Import_as_new_project_names_the_project_after_the_title()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();

        var fromUrl = await client.PostAsJsonAsync("/api/projects/import",
            new ProjectFromSpecificationRequest(null, Serve(SpecificationTests.PetsDocument)), Ct);
        var fromContent = await client.PostAsJsonAsync("/api/projects/import",
            new ProjectFromSpecificationRequest(SpecificationTests.PetsDocument, null), Ct);
        var first = await fromUrl.Content.ReadFromJsonAsync<SpecificationResponse>(Ct);
        var second = await fromContent.Content.ReadFromJsonAsync<SpecificationResponse>(Ct);
        var projects = await client.GetFromJsonAsync<List<ProjectResponse>>("/api/projects", Ct);

        Assert.Equal(HttpStatusCode.Created, fromUrl.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fromContent.StatusCode);
        Assert.NotEqual(first!.ProjectId, second!.ProjectId);
        Assert.NotNull(first.SourceUrl);
        Assert.Null(second.SourceUrl);
        Assert.All(projects!, p => Assert.Equal("Pets API", p.Name));
        Assert.Equal(2, projects!.Count);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("{}", "http://example.com/openapi.json")]
    public async Task Import_as_new_project_needs_exactly_one_source(string? content, string? url)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/projects/import", new ProjectFromSpecificationRequest(content, url), Ct);
        var projects = await client.GetFromJsonAsync<List<ProjectResponse>>("/api/projects", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(projects!);
    }

    [Theory]
    [InlineData("/documents/missing", "404")]
    [InlineData("/redirect", "redirects to http://169.254.169.254")]
    [InlineData("/large", "too large")]
    [InlineData("/binary", "didn't return a JSON document")]
    public async Task Url_problems_are_reported_on_the_url_field(string path, string expected)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/specifications/from-url", new SpecificationUrlRequest(factory.Target.Url(path), "X"), Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, Assert.Single(problem!.Errors["Url"]));
    }

    [Fact]
    public async Task Cloud_metadata_address_is_refused_even_though_localhost_is_allowed()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/projects/import",
            new ProjectFromSpecificationRequest(null, "http://169.254.169.254/latest/meta-data/"), Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("metadata", Assert.Single(problem!.Errors["Url"]));
    }

    [Fact]
    public async Task A_url_that_isnt_openapi_reports_the_document_errors()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var projectId = await client.CreateProjectAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/specifications/from-url", new SpecificationUrlRequest(factory.Target.Url("/echo"), null), Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem!.Errors.ContainsKey("Content"));
    }

    [Fact]
    public async Task Reimport_saves_the_new_version_and_lists_what_changed()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var url = Serve(SpecificationTests.PetsDocument);
        var created = await client.PostAsJsonAsync("/api/projects/import", new ProjectFromSpecificationRequest(null, url), Ct);
        var specification = (await created.Content.ReadFromJsonAsync<SpecificationResponse>(Ct))!;

        // Version bump, a new endpoint, a changed schema.
        factory.Target.Documents[url[(url.LastIndexOf('/') + 1)..]] = SpecificationTests.PetsDocument
            .Replace("\"2.4.0\"", "\"2.5.0\"")
            .Replace("\"maxLength\": 50", "\"maxLength\": 80")
            .Replace("\"paths\": {", """
                "paths": {
                  "/pets": { "get": { "responses": { "200": { "description": "OK" } } } },
                """);

        var response = await client.PostAsync($"/api/specifications/{specification.Id}/reimport", null, Ct);
        var result = await response.Content.ReadFromJsonAsync<ReimportResponse>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2.5.0", result!.Specification.Version);
        Assert.Equal(("2.4.0", "2.5.0"), (result.Changes.OldVersion, result.Changes.NewVersion));
        Assert.Equal(["GET /pets"], result.Changes.AddedEndpoints);
        Assert.Empty(result.Changes.RemovedEndpoints);
        Assert.Empty(result.Changes.ChangedEndpoints);
        Assert.Equal(["Pet"], result.Changes.ChangedSchemas);
        Assert.False(result.Changes.OtherChanges);
    }

    [Fact]
    public async Task Reimport_with_no_changes_says_so()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var created = await client.PostAsJsonAsync("/api/projects/import",
            new ProjectFromSpecificationRequest(null, Serve(SpecificationTests.PetsDocument)), Ct);
        var specification = (await created.Content.ReadFromJsonAsync<SpecificationResponse>(Ct))!;

        var result = await client.PostAsync($"/api/specifications/{specification.Id}/reimport", null, Ct);
        var body = await result.Content.ReadFromJsonAsync<ReimportResponse>(Ct);

        Assert.False(body!.Changes.HasChanges);
        Assert.Equal(specification.UpdatedAt, body.Specification.UpdatedAt);
    }

    [Fact]
    public async Task Reimport_needs_a_source_url()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var created = await client.PostAsJsonAsync("/api/projects/import",
            new ProjectFromSpecificationRequest(SpecificationTests.PetsDocument, null), Ct);
        var specification = (await created.Content.ReadFromJsonAsync<SpecificationResponse>(Ct))!;

        var response = await client.PostAsync($"/api/specifications/{specification.Id}/reimport", null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_users_specification_cant_be_reimported()
    {
        var (owner, _) = await factory.CreateSignedInClientAsync();
        var (other, _) = await factory.CreateSignedInClientAsync();
        var created = await owner.PostAsJsonAsync("/api/projects/import",
            new ProjectFromSpecificationRequest(null, Serve(SpecificationTests.PetsDocument)), Ct);
        var specification = (await created.Content.ReadFromJsonAsync<SpecificationResponse>(Ct))!;

        var response = await other.PostAsync($"/api/specifications/{specification.Id}/reimport", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
