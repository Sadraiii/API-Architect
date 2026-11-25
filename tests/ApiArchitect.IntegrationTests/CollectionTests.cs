using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ApiArchitect.Api.Features.Collections;
using ApiArchitect.Api.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace ApiArchitect.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CollectionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SavedRequestInput SampleRequest(Guid? folderId = null) => new(
        "Get order",
        "GET",
        "/orders/{{orderId}}",
        folderId,
        [new KeyValueDto("Accept", "application/json"), new KeyValueDto("X-Debug", "1", Enabled: false)],
        [new KeyValueDto("expand", "lines")],
        Body: null,
        ContentType: null,
        new AuthDto(AuthTypes.Bearer, "{{token}}", null, null));

    [Fact]
    public async Task Collection_tree_lists_folders_and_requests()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        var folder = await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput("Orders", null));
        var sub = await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput("Lines", folder.Id));
        var request = await PostAsync<SavedRequestResponse>(client, $"/api/collections/{collection.Id}/requests", SampleRequest(sub.Id));

        var detail = await client.GetFromJsonAsync<CollectionDetail>($"/api/collections/{collection.Id}", Ct);
        var summaries = await client.GetFromJsonAsync<List<CollectionSummary>>($"/api/projects/{collection.ProjectId}/collections", Ct);

        Assert.Equal([folder.Id, sub.Id], detail!.Folders.Select(f => f.Id));
        Assert.Equal(folder.Id, detail.Folders[1].ParentFolderId);
        Assert.Equal(sub.Id, Assert.Single(detail.Requests).FolderId);
        Assert.Equal(request.Id, detail.Requests[0].Id);
        Assert.Equal(1, Assert.Single(summaries!).RequestCount);
    }

    [Fact]
    public async Task Saved_request_round_trips_every_field()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        var input = SampleRequest() with { Method = "POST", Body = "{\"a\":1}", ContentType = "application/json" };

        var created = await client.PostAsJsonAsync($"/api/collections/{collection.Id}/requests", input, Ct);
        var fetched = await client.GetFromJsonAsync<SavedRequestResponse>(created.Headers.Location, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("POST", fetched!.Method);
        Assert.Equal("/orders/{{orderId}}", fetched.Url);
        Assert.Equal(input.Headers, fetched.Headers);
        Assert.Equal(input.QueryParameters, fetched.QueryParameters);
        Assert.Equal("{\"a\":1}", fetched.Body);
        Assert.Equal("application/json", fetched.ContentType);
        Assert.Equal(new AuthDto(AuthTypes.Bearer, "{{token}}", null, null), fetched.Auth);
    }

    [Fact]
    public async Task Update_moves_a_request_into_a_folder()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        var folder = await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput("Orders", null));
        var request = await PostAsync<SavedRequestResponse>(client, $"/api/collections/{collection.Id}/requests", SampleRequest());

        var response = await client.PutAsJsonAsync($"/api/requests/{request.Id}", SampleRequest(folder.Id) with { Name = "Renamed" }, Ct);
        var updated = await response.Content.ReadFromJsonAsync<SavedRequestResponse>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(folder.Id, updated!.FolderId);
        Assert.Equal("Renamed", updated.Name);
    }

    [Fact]
    public async Task A_folder_from_another_collection_is_rejected()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var first = await CreateCollectionAsync(client);
        var second = await CreateCollectionAsync(client, first.ProjectId);
        var otherFolder = await PostAsync<FolderResponse>(client, $"/api/collections/{second.Id}/folders", new FolderInput("Elsewhere", null));

        var request = await client.PostAsJsonAsync($"/api/collections/{first.Id}/requests", SampleRequest(otherFolder.Id), Ct);
        var folder = await client.PostAsJsonAsync($"/api/collections/{first.Id}/folders", new FolderInput("Child", otherFolder.Id), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, request.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, folder.StatusCode);
    }

    [Fact]
    public async Task A_folder_cannot_move_into_its_own_subfolder()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        var parent = await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput("Parent", null));
        var child = await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput("Child", parent.Id));

        var intoChild = await client.PutAsJsonAsync($"/api/folders/{parent.Id}", new FolderInput("Parent", child.Id), Ct);
        var intoSelf = await client.PutAsJsonAsync($"/api/folders/{parent.Id}", new FolderInput("Parent", parent.Id), Ct);
        var problem = await intoChild.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, intoChild.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, intoSelf.StatusCode);
        Assert.Contains("ParentFolderId", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Folders_nest_at_most_ten_levels()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        Guid? parent = null;
        for (var level = 1; level <= CollectionRules.MaxFolderDepth; level++)
        {
            parent = (await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput($"L{level}", parent))).Id;
        }

        var tooDeep = await client.PostAsJsonAsync($"/api/collections/{collection.Id}/folders", new FolderInput("L11", parent), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_folder_deletes_its_subfolders_and_requests()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        var parent = await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput("Parent", null));
        var child = await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput("Child", parent.Id));
        var nested = await PostAsync<SavedRequestResponse>(client, $"/api/collections/{collection.Id}/requests", SampleRequest(child.Id));
        var topLevel = await PostAsync<SavedRequestResponse>(client, $"/api/collections/{collection.Id}/requests", SampleRequest());

        var delete = await client.DeleteAsync($"/api/folders/{parent.Id}", Ct);
        var detail = await client.GetFromJsonAsync<CollectionDetail>($"/api/collections/{collection.Id}", Ct);
        var nestedGet = await client.GetAsync($"/api/requests/{nested.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(detail!.Folders);
        Assert.Equal(topLevel.Id, Assert.Single(detail.Requests).Id);
        Assert.Equal(HttpStatusCode.NotFound, nestedGet.StatusCode);
    }

    [Fact]
    public async Task Duplicate_copies_the_request_into_the_same_folder()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        var folder = await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput("Orders", null));
        var original = await PostAsync<SavedRequestResponse>(client, $"/api/collections/{collection.Id}/requests", SampleRequest(folder.Id));

        var copy = await PostAsync<SavedRequestResponse>(client, $"/api/requests/{original.Id}/duplicate", new { });

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Get order (copy)", copy.Name);
        Assert.Equal(folder.Id, copy.FolderId);
        Assert.Equal(original.Headers, copy.Headers);
        Assert.Equal(original.Auth, copy.Auth);
    }

    [Theory]
    [InlineData("TRACE", "Method")]
    [InlineData("GET", "Auth.KeyName", AuthTypes.ApiKey)]
    [InlineData("GET", "Headers[0].Name", AuthTypes.Inherit, true)]
    public async Task Invalid_request_returns_validation_problem(
        string method, string expectedKey, string authType = AuthTypes.Inherit, bool longHeaderName = false)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        var input = SampleRequest() with
        {
            Method = method,
            Auth = new AuthDto(authType, "v", null, null),
            Headers = longHeaderName ? [new KeyValueDto(new string('h', CollectionRules.KeyMaxLength + 1), "v")] : [],
        };

        var response = await client.PostAsJsonAsync($"/api/collections/{collection.Id}/requests", input, Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem!.Errors.Keys, key => key.Equals(expectedKey, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Another_users_collections_folders_and_requests_are_not_found_for_every_operation()
    {
        var (alice, _) = await factory.CreateSignedInClientAsync();
        var (mallory, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(alice);
        var folder = await PostAsync<FolderResponse>(alice, $"/api/collections/{collection.Id}/folders", new FolderInput("Orders", null));
        var request = await PostAsync<SavedRequestResponse>(alice, $"/api/collections/{collection.Id}/requests", SampleRequest());
        var project = $"/api/projects/{collection.ProjectId}";
        var postman = PostmanSample();

        HttpResponseMessage[] responses =
        [
            await mallory.GetAsync($"{project}/collections", Ct),
            await mallory.PostAsJsonAsync($"{project}/collections", new CollectionInput("Mine now", null), Ct),
            await mallory.PostAsJsonAsync($"{project}/collections/import", postman, Ct),
            await mallory.GetAsync($"/api/collections/{collection.Id}", Ct),
            await mallory.PutAsJsonAsync($"/api/collections/{collection.Id}", new CollectionInput("Taken", null), Ct),
            await mallory.GetAsync($"/api/collections/{collection.Id}/export", Ct),
            await mallory.PostAsJsonAsync($"/api/collections/{collection.Id}/folders", new FolderInput("X", null), Ct),
            await mallory.PutAsJsonAsync($"/api/folders/{folder.Id}", new FolderInput("Taken", null), Ct),
            await mallory.GetAsync($"/api/collections/{collection.Id}/requests", Ct),
            await mallory.PostAsJsonAsync($"/api/collections/{collection.Id}/requests", SampleRequest(), Ct),
            await mallory.GetAsync($"/api/requests/{request.Id}", Ct),
            await mallory.PutAsJsonAsync($"/api/requests/{request.Id}", SampleRequest(), Ct),
            await mallory.PostAsJsonAsync($"/api/requests/{request.Id}/duplicate", new { }, Ct),
            await mallory.DeleteAsync($"/api/requests/{request.Id}", Ct),
            await mallory.DeleteAsync($"/api/folders/{folder.Id}", Ct),
            await mallory.DeleteAsync($"/api/collections/{collection.Id}", Ct),
        ];
        var stillThere = await alice.GetFromJsonAsync<CollectionDetail>($"/api/collections/{collection.Id}", Ct);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(collection.Name, stillThere!.Name);
        Assert.Single(stillThere.Folders);
        Assert.Single(stillThere.Requests);
    }

    [Fact]
    public async Task Deleting_the_project_deletes_its_collections()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        await PostAsync<SavedRequestResponse>(client, $"/api/collections/{collection.Id}/requests", SampleRequest());

        await client.DeleteAsync($"/api/projects/{collection.ProjectId}", Ct);
        var get = await client.GetAsync($"/api/collections/{collection.Id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Postman_import_builds_folders_and_requests_and_reports_what_was_skipped()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await PostAsync<ProjectResponse>(client, "/api/projects", new ProjectRequest("Orders API", null, null));

        var response = await client.PostAsJsonAsync($"/api/projects/{project.Id}/collections/import", PostmanSample(), Ct);
        var import = await response.Content.ReadFromJsonAsync<CollectionImportResponse>(Ct);
        var detail = await client.GetFromJsonAsync<CollectionDetail>($"/api/collections/{import!.Collection.Id}", Ct);
        var create = await client.GetFromJsonAsync<SavedRequestResponse>(
            $"/api/requests/{detail!.Requests.Single(r => r.Name == "Create order").Id}", Ct);
        var list = await client.GetFromJsonAsync<SavedRequestResponse>(
            $"/api/requests/{detail.Requests.Single(r => r.Name == "List orders").Id}", Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Shop", import.Collection.Name);
        Assert.Equal(3, import.Collection.RequestCount);
        Assert.Equal("Orders", Assert.Single(detail.Folders).Name);
        Assert.Contains(import.Warnings, w => w.Contains("Collection variables"));
        Assert.Contains(import.Warnings, w => w.Contains("formdata"));

        Assert.Equal("POST", create!.Method);
        Assert.Equal("{{baseUrl}}/orders", create.Url);
        Assert.Equal("application/json", create.ContentType);
        Assert.Equal("{\"sku\":\"A1\"}", create.Body);
        Assert.Equal(new AuthDto(AuthTypes.ApiKey, "{{apiKey}}", "X-Api-Key", ApiKeyLocations.Header), create.Auth);

        Assert.Equal(detail.Folders[0].Id, list!.FolderId);
        Assert.Equal("{{baseUrl}}/orders", list.Url);
        Assert.Equal([new KeyValueDto("page", "1"), new KeyValueDto("status", "open shipped", false)], list.QueryParameters);
        Assert.Equal(new AuthDto(AuthTypes.Bearer, "{{token}}", null, null), list.Auth);
    }

    [Fact]
    public async Task Export_then_import_gives_the_same_collection()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var collection = await CreateCollectionAsync(client);
        var folder = await PostAsync<FolderResponse>(client, $"/api/collections/{collection.Id}/folders", new FolderInput("Orders", null));
        var original = await PostAsync<SavedRequestResponse>(client, $"/api/collections/{collection.Id}/requests",
            SampleRequest(folder.Id) with { Method = "PUT", Body = "{}", ContentType = "application/json" });

        var export = await client.GetAsync($"/api/collections/{collection.Id}/export", Ct);
        var json = await export.Content.ReadAsStringAsync(Ct);
        var reimport = await client.PostAsync($"/api/projects/{collection.ProjectId}/collections/import",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"), Ct);
        var imported = await reimport.Content.ReadFromJsonAsync<CollectionImportResponse>(Ct);
        var detail = await client.GetFromJsonAsync<CollectionDetail>($"/api/collections/{imported!.Collection.Id}", Ct);
        var copy = await client.GetFromJsonAsync<SavedRequestResponse>($"/api/requests/{Assert.Single(detail!.Requests).Id}", Ct);

        Assert.Equal("application/json", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains(".postman_collection.json", export.Content.Headers.ContentDisposition?.FileName);
        Assert.Empty(imported.Warnings);
        Assert.Equal("Orders", Assert.Single(detail.Folders).Name);
        Assert.Equal(detail.Folders[0].Id, copy!.FolderId);
        Assert.Equal(
            (original.Name, original.Method, original.Url, original.Body, original.ContentType, original.Auth),
            (copy.Name, copy.Method, copy.Url, copy.Body, copy.ContentType, copy.Auth));
        Assert.Equal(original.Headers, copy.Headers);
        Assert.Equal(original.QueryParameters, copy.QueryParameters);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"info\":{\"name\":\"x\",\"schema\":\"https://schema.getpostman.com/json/collection/v1.0.0/collection.json\"},\"item\":[]}")]
    [InlineData("{\"openapi\":\"3.1.0\"}")]
    public async Task Import_rejects_files_that_are_not_postman_v2_collections(string body)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var project = await PostAsync<ProjectResponse>(client, "/api/projects", new ProjectRequest("P", null, null));

        var response = await client.PostAsync($"/api/projects/{project.Id}/collections/import",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    internal static JsonNode PostmanSample() => JsonNode.Parse("""
        {
          "info": { "name": "Shop", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "variable": [ { "key": "baseUrl", "value": "https://shop.example.com" } ],
          "item": [
            {
              "name": "Orders",
              "item": [
                {
                  "name": "List orders",
                  "request": {
                    "method": "GET",
                    "auth": { "type": "bearer", "bearer": [ { "key": "token", "value": "{{token}}", "type": "string" } ] },
                    "url": {
                      "raw": "{{baseUrl}}/orders?page=1",
                      "query": [ { "key": "page", "value": "1" }, { "key": "status", "value": "open shipped", "disabled": true } ]
                    }
                  }
                }
              ]
            },
            {
              "name": "Create order",
              "request": {
                "method": "POST",
                "header": [ { "key": "Content-Type", "value": "application/json" } ],
                "auth": { "type": "apikey", "apikey": [ { "key": "key", "value": "X-Api-Key" }, { "key": "value", "value": "{{apiKey}}" }, { "key": "in", "value": "header" } ] },
                "body": { "mode": "raw", "raw": "{\"sku\":\"A1\"}", "options": { "raw": { "language": "json" } } },
                "url": "{{baseUrl}}/orders"
              }
            },
            {
              "name": "Upload invoice",
              "request": { "method": "POST", "url": "{{baseUrl}}/invoices", "body": { "mode": "formdata", "formdata": [] } }
            }
          ]
        }
        """)!;

    private static async Task<CollectionSummary> CreateCollectionAsync(HttpClient client, Guid? projectId = null)
    {
        projectId ??= (await PostAsync<ProjectResponse>(client, "/api/projects", new ProjectRequest("Orders API", null, null))).Id;
        return await PostAsync<CollectionSummary>(client, $"/api/projects/{projectId}/collections", new CollectionInput("Smoke tests", null));
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}
