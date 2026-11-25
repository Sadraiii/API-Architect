using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using ApiArchitect.Api.Features.Designs;
using ApiArchitect.Api.Features.Projects;
using ApiArchitect.Api.Features.Specifications;
using Microsoft.AspNetCore.Mvc;

namespace ApiArchitect.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DesignTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_blank_design_takes_its_title_and_namespace_from_the_name()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/designs", new CreateDesignRequest(" orders service ", null), Ct);
        var design = await response.Content.ReadFromJsonAsync<DesignResponse>(Ct);
        var fetched = await client.GetFromJsonAsync<DesignResponse>(response.Headers.Location, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("orders service", "orders service", "OrdersService.Api"),
            (design!.Name, design.Document.Title, design.Document.Namespace));
        Assert.Empty(design.Document.Endpoints);
        Assert.Equal(design.Id, fetched!.Id);
        Assert.Equal(design.Document.Namespace, fetched.Document.Namespace);
    }

    [Fact]
    public async Task The_example_design_produces_a_valid_OpenAPI_document()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var design = await CreateExampleAsync(client);

        var response = await client.GetAsync($"/api/designs/{design.Id}/openapi", Ct);
        var (parsed, errors) = OpenApiImport.Read(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(errors);
        Assert.Equal(("Bookstore API", "1.0.0", DesignOpenApiWriter.OpenApiVersion), (parsed!.Title, parsed.Version, parsed.OpenApiVersion));
    }

    [Fact]
    public async Task Code_preview_and_zip_contain_the_same_generated_project()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var design = await CreateExampleAsync(client);

        var files = await client.GetFromJsonAsync<List<CSharpProjectGenerator.GeneratedFile>>($"/api/designs/{design.Id}/code", Ct);
        var zipResponse = await client.GetAsync($"/api/designs/{design.Id}/code/zip", Ct);
        using var zip = new ZipArchive(await zipResponse.Content.ReadAsStreamAsync(Ct));

        Assert.Equal("application/zip", zipResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Bookstore.Api.zip", zipResponse.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(files!.Select(f => f.Path), zip.Entries.Select(e => e.FullName));
        Assert.Contains("Bookstore.Api/Endpoints/BooksEndpoints.cs", files!.Select(f => f.Path));
    }

    [Fact]
    public async Task Saving_an_invalid_design_returns_every_problem_and_keeps_the_old_one()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var design = await CreateExampleAsync(client);
        var broken = design.Document with
        {
            Endpoints =
            [
                new() { Resource = "Books", Method = "GET", Route = "/api/books/{id}", Name = "GetBook", ResponseModel = "Missing" },
                new() { Resource = "Books", Method = "GET", Route = "/api/books", Name = "ListBooks", RequestModel = "Book" },
            ],
        };

        var response = await client.PutAsJsonAsync($"/api/designs/{design.Id}", new UpdateDesignRequest("Bookstore", broken), Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        var stored = await client.GetFromJsonAsync<DesignResponse>($"/api/designs/{design.Id}", Ct);
        var messages = problem!.Errors.Values.SelectMany(m => m).ToList();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(messages, m => m.Contains("needs a path parameter called id"));
        Assert.Contains(messages, m => m.Contains("response model Missing doesn't exist"));
        Assert.Contains(messages, m => m.Contains("GET requests don't have a body"));
        Assert.Equal(5, stored!.Document.Endpoints.Count);
    }

    [Fact]
    public async Task Saving_a_valid_design_replaces_the_document()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var design = await CreateExampleAsync(client);
        var changed = design.Document with { Version = "2.0.0", Endpoints = design.Document.Endpoints[..2] };

        var response = await client.PutAsJsonAsync($"/api/designs/{design.Id}", new UpdateDesignRequest("Books v2", changed), Ct);
        var stored = await client.GetFromJsonAsync<DesignResponse>($"/api/designs/{design.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("Books v2", "2.0.0", 2), (stored!.Name, stored.Document.Version, stored.Document.Endpoints.Count));
    }

    [Fact]
    public async Task Saving_as_a_specification_twice_updates_the_same_specification()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var design = await CreateExampleAsync(client);

        var first = await SaveAsSpecificationAsync(client, design.Id);
        await client.PutAsJsonAsync($"/api/designs/{design.Id}",
            new UpdateDesignRequest(design.Name, design.Document with { Version = "1.1.0" }), Ct);
        var second = await SaveAsSpecificationAsync(client, design.Id);
        var specifications = await client.GetFromJsonAsync<List<SpecificationResponse>>(
            $"/api/projects/{design.ProjectId}/specifications", Ct);
        var linked = await client.GetFromJsonAsync<DesignResponse>($"/api/designs/{design.Id}", Ct);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(("1.0.0", "1.1.0"), (first.Version, second.Version));
        Assert.Equal([first.Id], specifications!.Select(s => s.Id));
        Assert.Equal(first.Id, linked!.SpecificationId);
    }

    [Fact]
    public async Task Deleting_the_saved_specification_unlinks_it_and_keeps_the_design()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var design = await CreateExampleAsync(client);
        var specification = await SaveAsSpecificationAsync(client, design.Id);

        await client.DeleteAsync($"/api/specifications/{specification.Id}", Ct);
        var after = await client.GetFromJsonAsync<DesignResponse>($"/api/designs/{design.Id}", Ct);

        Assert.Null(after!.SpecificationId);
    }

    [Fact]
    public async Task List_returns_the_projects_designs_newest_first()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var first = await CreateExampleAsync(client);
        var second = await CreateDesignAsync(client, first.ProjectId, "Second", "blank");

        var list = await client.GetFromJsonAsync<List<DesignSummaryResponse>>($"/api/projects/{first.ProjectId}/designs", Ct);

        Assert.Equal([second.Id, first.Id], list!.Select(d => d.Id));
    }

    [Fact]
    public async Task Another_users_design_is_not_found_for_every_operation()
    {
        var (alice, _) = await factory.CreateSignedInClientAsync();
        var (mallory, _) = await factory.CreateSignedInClientAsync();
        var design = await CreateExampleAsync(alice);
        var url = $"/api/designs/{design.Id}";

        var responses = new[]
        {
            await mallory.GetAsync($"/api/projects/{design.ProjectId}/designs", Ct),
            await mallory.PostAsJsonAsync($"/api/projects/{design.ProjectId}/designs", new CreateDesignRequest("Mine", null), Ct),
            await mallory.GetAsync(url, Ct),
            await mallory.PutAsJsonAsync(url, new UpdateDesignRequest("Mine", design.Document), Ct),
            await mallory.GetAsync($"{url}/openapi", Ct),
            await mallory.GetAsync($"{url}/code", Ct),
            await mallory.GetAsync($"{url}/code/zip", Ct),
            await mallory.PostAsync($"{url}/specification", null, Ct),
            await mallory.DeleteAsync(url, Ct),
        };
        var stillThere = await alice.GetAsync(url, Ct);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    private static async Task<DesignResponse> CreateExampleAsync(HttpClient client)
    {
        var project = await CreateProjectAsync(client);
        return await CreateDesignAsync(client, project.Id, "Bookstore", "example");
    }

    private static async Task<DesignResponse> CreateDesignAsync(HttpClient client, Guid projectId, string name, string startFrom)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/designs", new CreateDesignRequest(name, startFrom), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DesignResponse>(Ct))!;
    }

    private static async Task<SpecificationResponse> SaveAsSpecificationAsync(HttpClient client, Guid designId)
    {
        var response = await client.PostAsync($"/api/designs/{designId}/specification", null, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SpecificationResponse>(Ct))!;
    }

    private static async Task<ProjectResponse> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new ProjectRequest("Books", null, "net11.0"), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!;
    }
}
