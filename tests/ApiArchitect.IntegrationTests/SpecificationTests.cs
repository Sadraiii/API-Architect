using System.Net;
using System.Net.Http.Json;
using ApiArchitect.Api.Features.Projects;
using ApiArchitect.Api.Features.Specifications;
using Microsoft.AspNetCore.Mvc;

namespace ApiArchitect.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SpecificationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Deliberately unusual formatting and key order: export must give back exactly these characters.
    private const string PetsDocument = """
        {
          "openapi": "3.1.1",
          "info": { "version": "2.4.0", "title": "Pets API" },
          "paths": {
            "/pets/{id}": {
              "get": {
                "tags": ["Pets"],
                "operationId": "getPet",
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }],
                "responses": {
                  "200": { "description": "OK", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } } },
                  "404": { "description": "Not found" }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Pet": {
                "type": "object",
                "required": ["id", "name"],
                "properties": { "id": { "type": "integer" }, "name": { "type": "string", "maxLength": 50 } }
              }
            }
          }
        }
        """;

    [Fact]
    public async Task Import_then_get_and_export_returns_the_original_document()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);

        var created = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/specifications", new SpecificationRequest("  Pets  ", PetsDocument), Ct);
        var specification = await created.Content.ReadFromJsonAsync<SpecificationResponse>(Ct);
        var fetched = await client.GetFromJsonAsync<SpecificationResponse>(created.Headers.Location, Ct);
        var exported = await client.GetAsync($"/api/specifications/{specification!.Id}/document", Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(("Pets", "Pets API", "2.4.0", "3.1.1", project.Id),
            (specification.Name, specification.Title, specification.Version, specification.OpenApiVersion, specification.ProjectId));
        Assert.Equal(specification, fetched);
        Assert.Equal("application/json", exported.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PetsDocument, await exported.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Importing_touches_the_projects_UpdatedAt()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);

        var specification = await ImportAsync(client, project.Id, "Pets", PetsDocument);
        var after = await client.GetFromJsonAsync<ProjectResponse>($"/api/projects/{project.Id}", Ct);

        Assert.Equal(specification.UpdatedAt, after!.UpdatedAt);
    }

    [Fact]
    public async Task List_returns_the_projects_specifications_newest_first()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);
        var otherProject = await CreateProjectAsync(client);
        var first = await ImportAsync(client, project.Id, "First", PetsDocument);
        var second = await ImportAsync(client, project.Id, "Second", PetsDocument);
        await ImportAsync(client, otherProject.Id, "Elsewhere", PetsDocument);

        var list = await client.GetFromJsonAsync<List<SpecificationResponse>>($"/api/projects/{project.Id}/specifications", Ct);

        Assert.Equal([second.Id, first.Id], list!.Select(s => s.Id));
    }

    [Fact]
    public async Task Replace_updates_the_document_and_the_fields_read_from_it()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);
        var specification = await ImportAsync(client, project.Id, "Pets", PetsDocument);
        var newVersion = PetsDocument.Replace("\"2.4.0\"", "\"3.0.0\"").Replace("Pets API", "Pet Store API");

        var response = await client.PutAsJsonAsync(
            $"/api/specifications/{specification.Id}", new SpecificationRequest("Pet store", newVersion), Ct);
        var updated = await response.Content.ReadFromJsonAsync<SpecificationResponse>(Ct);
        var exported = await client.GetStringAsync($"/api/specifications/{specification.Id}/document", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("Pet store", "Pet Store API", "3.0.0"), (updated!.Name, updated.Title, updated.Version));
        Assert.True(updated.UpdatedAt >= specification.UpdatedAt);
        Assert.Equal(newVersion, exported);
    }

    [Fact]
    public async Task Delete_removes_the_specification()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);
        var specification = await ImportAsync(client, project.Id, "Pets", PetsDocument);

        var delete = await client.DeleteAsync($"/api/specifications/{specification.Id}", Ct);
        var get = await client.GetAsync($"/api/specifications/{specification.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_project_deletes_its_specifications()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);
        var specification = await ImportAsync(client, project.Id, "Pets", PetsDocument);

        await client.DeleteAsync($"/api/projects/{project.Id}", Ct);
        var get = await client.GetAsync($"/api/specifications/{specification.Id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Another_users_specification_is_not_found_for_every_operation()
    {
        var (alice, _) = await factory.CreateSignedInClientAsync();
        var (mallory, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(alice);
        var specification = await ImportAsync(alice, project.Id, "Pets", PetsDocument);
        var url = $"/api/specifications/{specification.Id}";
        var projectUrl = $"/api/projects/{project.Id}/specifications";

        var list = await mallory.GetAsync(projectUrl, Ct);
        var import = await mallory.PostAsJsonAsync(projectUrl, new SpecificationRequest("Planted", PetsDocument), Ct);
        var get = await mallory.GetAsync(url, Ct);
        var document = await mallory.GetAsync($"{url}/document", Ct);
        var put = await mallory.PutAsJsonAsync(url, new SpecificationRequest("Taken over", PetsDocument), Ct);
        var delete = await mallory.DeleteAsync(url, Ct);
        var aliceList = await alice.GetFromJsonAsync<List<SpecificationResponse>>(projectUrl, Ct);

        Assert.All([list, import, get, document, put, delete], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal([("Pets", specification.Id)], aliceList!.Select(s => (s.Name, s.Id)));
    }

    [Fact]
    public async Task Importing_into_a_missing_project_is_not_found()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{Guid.CreateVersion7()}/specifications", new SpecificationRequest("Pets", PetsDocument), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public static TheoryData<string, string> InvalidDocuments => new()
    {
        { "not json", "Not JSON" },
        { "{ \"openapi\": \"3.1.0\", \"info\": { \"title\": ", "Truncated JSON" },
        { "[1, 2, 3]", "JSON array" },
        { "{ \"swagger\": \"2.0\", \"info\": { \"title\": \"Old\", \"version\": \"1\" }, \"paths\": {} }", "Swagger 2.0" },
        { "{ \"openapi\": \"3.1.0\", \"paths\": {} }", "No info" },
        { "{ \"openapi\": \"3.1.0\", \"info\": { \"title\": \"\", \"version\": \"1\" }, \"paths\": {} }", "Empty title" },
        { "openapi: 3.1.0\ninfo:\n  title: YAML\n  version: '1'\npaths: {}", "YAML" },
    };

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public async Task Invalid_document_returns_a_validation_problem_on_Content(string content, string because)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/specifications", new SpecificationRequest("Bad", content), Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, because);
        Assert.NotEmpty(problem!.Errors["Content"]);
    }

    [Fact]
    public async Task Missing_name_returns_a_validation_problem()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/specifications", new SpecificationRequest("", PetsDocument), Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem!.Errors.Keys, key => key.Equals("Name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task External_references_are_stored_without_being_fetched()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);
        // If the server followed this $ref it would hang on (or reach) a cloud metadata address.
        var document = PetsDocument.Replace("#/components/schemas/Pet", "http://169.254.169.254/latest/meta-data/pet.json");

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/specifications", new SpecificationRequest("External", document), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<ProjectResponse> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new ProjectRequest("Pets", null, "net11.0"), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!;
    }

    private static async Task<SpecificationResponse> ImportAsync(HttpClient client, Guid projectId, string name, string content)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/specifications", new SpecificationRequest(name, content), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SpecificationResponse>(Ct))!;
    }
}
