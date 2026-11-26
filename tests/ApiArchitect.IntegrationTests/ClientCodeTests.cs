using System.Net;
using System.Net.Http.Json;
using ApiArchitect.Api.Features.ClientCode;
using ApiArchitect.Api.Features.Projects;
using ApiArchitect.Api.Features.Specifications;
using Microsoft.AspNetCore.Mvc;

namespace ApiArchitect.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ClientCodeTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Document = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Pets API", "version": "1.0" },
          "servers": [{ "url": "https://pets.example.com/v1" }],
          "security": [{ "key": [] }],
          "paths": {
            "/pets": {
              "get": {
                "operationId": "listPets",
                "parameters": [{ "name": "limit", "in": "query", "schema": { "type": "integer" } }],
                "responses": { "200": { "description": "OK", "content": { "application/json": { "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Pet" } } } } } }
              },
              "post": {
                "operationId": "addPet",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } } },
                "responses": { "201": { "description": "Created" } }
              }
            },
            "/pets/{id}": {
              "get": {
                "operationId": "getPet",
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "integer", "format": "int64" } }],
                "responses": { "200": { "description": "OK", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } } } }
              }
            }
          },
          "components": {
            "securitySchemes": { "key": { "type": "apiKey", "in": "header", "name": "X-Api-Key" } },
            "schemas": {
              "Pet": { "type": "object", "properties": { "id": { "type": "integer", "format": "int64" }, "pet_name": { "type": "string" } } }
            }
          }
        }
        """;

    [Fact]
    public async Task Console_template_gives_a_client_models_and_program_with_steps()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var specification = await ImportAsync(client);

        var result = await GenerateAsync(client, specification.Id,
            new ClientCodeRequest("console", "PetsConnector", ["GET /pets", "get /pets/{id}"]));

        Assert.Equal(["ApiClient.cs", "Models.cs", "Program.cs"], result.Files.Select(f => f.FileName));
        var apiClient = result.Files[0].Content;
        Assert.Contains("namespace PetsConnector;", apiClient);
        Assert.Contains("public async Task<List<Pet>?> ListPetsAsync(int? limit = null, CancellationToken cancellationToken = default)", apiClient);
        Assert.Contains("public async Task<Pet?> GetPetAsync(long id, CancellationToken cancellationToken = default)", apiClient);
        Assert.DoesNotContain("AddPetAsync", apiClient);
        Assert.Contains("[JsonPropertyName(\"pet_name\")]", result.Files[1].Content);

        var program = result.Files[2].Content;
        Assert.Contains("new Uri(\"https://pets.example.com/v1/\")", program);
        Assert.Contains("Environment.GetEnvironmentVariable(\"PETSCONNECTOR_API_KEY\")", program);
        Assert.Contains("DefaultRequestHeaders.Add(\"X-Api-Key\", apiKey)", program);
        Assert.Contains("    var listPets = await api.ListPetsAsync(", program);
        Assert.Contains("    // var getPet = await api.GetPetAsync(id: 1,", program);

        Assert.Contains(result.Steps, s => s.Contains("`Console App`") && s.Contains("`PetsConnector`"));
        Assert.Contains(result.Steps, s => s.Contains(".NET 11"));
        Assert.Contains(result.Steps, s => s.Contains("PETSCONNECTOR_API_KEY"));
        Assert.Empty(result.Notes);
    }

    [Fact]
    public async Task Worker_template_adds_the_http_package_settings_and_user_secrets()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var specification = await ImportAsync(client);

        var result = await GenerateAsync(client, specification.Id,
            new ClientCodeRequest("worker", "Pets.Sync", ["POST /pets", "GET /nope"]));

        Assert.Equal(["ApiClient.cs", "Models.cs", "Program.cs", "Worker.cs", "appsettings.json", "secrets.json"],
            result.Files.Select(f => f.FileName));
        Assert.Contains("builder.Services.AddHttpClient(\"Api\"", result.Files[2].Content);
        Assert.Contains("PeriodicTimer", result.Files[3].Content);
        Assert.Contains("\"BaseUrl\": \"https://pets.example.com/v1/\"", result.Files[4].Content);
        Assert.Contains(result.Steps, s => s.Contains("`Worker Service`"));
        Assert.Contains(result.Steps, s => s.Contains("`Microsoft.Extensions.Http`"));
        Assert.Contains(result.Steps, s => s.Contains("`Manage User Secrets`"));
        Assert.Equal(["Skipped GET /nope: it isn't in the specification."], result.Notes);
    }

    [Theory]
    [InlineData("console", "class", "GET /pets", "ProjectName")]
    [InlineData("console", "1Pets", "GET /pets", "ProjectName")]
    [InlineData("desktop", "Pets", "GET /pets", "Template")]
    [InlineData("console", "Pets", "GET /missing", "Operations")]
    public async Task Bad_requests_are_400s(string template, string projectName, string operation, string field)
    {
        var (client, _) = await factory.CreateSignedInClientAsync();
        var specification = await ImportAsync(client);

        var response = await client.PostAsJsonAsync($"/api/specifications/{specification.Id}/client-code",
            new ClientCodeRequest(template, projectName, [operation]), Ct);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Another_users_specification_is_not_found()
    {
        var (owner, _) = await factory.CreateSignedInClientAsync();
        var (other, _) = await factory.CreateSignedInClientAsync();
        var specification = await ImportAsync(owner);

        var response = await other.PostAsJsonAsync($"/api/specifications/{specification.Id}/client-code",
            new ClientCodeRequest("console", "Pets", ["GET /pets"]), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<SpecificationResponse> ImportAsync(HttpClient client)
    {
        var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequest("Pets", null, null), Ct))
            .Content.ReadFromJsonAsync<ProjectResponse>(Ct);
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{project!.Id}/specifications", new SpecificationRequest("Pets", Document), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SpecificationResponse>(Ct))!;
    }

    private static async Task<ClientCodeResponse> GenerateAsync(HttpClient client, Guid specificationId, ClientCodeRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/specifications/{specificationId}/client-code", request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClientCodeResponse>(Ct))!;
    }
}
