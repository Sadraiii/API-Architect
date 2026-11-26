using System.Security.Claims;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Features.Specifications;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace ApiArchitect.Api.Features.ClientCode;

public static class ClientCodeEndpoints
{
    public static void MapClientCodeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/specifications/{id:guid}/client-code", Generate)
            .WithTags("Client code")
            .RequireAuthorization()
            .WithSummary("Generate C# to call the API from a Console App or a Worker Service, with Visual Studio steps.");
    }

    private static async Task<Results<Ok<ClientCodeResponse>, ValidationProblem, NotFound>> Generate(
        Guid id, ClientCodeRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var content = await db.ApiSpecifications
            .Where(s => s.Id == id && s.Project.OwnerId == userId)
            .Select(s => s.Content)
            .SingleOrDefaultAsync(cancellationToken);
        if (content is null)
        {
            return TypedResults.NotFound();
        }

        if (request.ProjectName.Split('.').Any(CSharpNames.IsKeyword))
        {
            return Problem(nameof(request.ProjectName), "The project name can't be a C# keyword such as 'class'.");
        }

        // The document was checked on import, so a failure here means it was stored by an older version.
        OpenApiDocument? document;
        try
        {
            document = OpenApiImport.Parse(content).Document;
        }
        catch (Exception ex) when (ex is OpenApiException or System.Text.Json.JsonException or FormatException)
        {
            document = null;
        }

        if (document is null)
        {
            return Problem("Specification", "The stored specification can't be read. Import it again.");
        }

        var result = ClientCodeGenerator.Generate(document, request);
        return result is null
            ? Problem(nameof(request.Operations), "None of the chosen operations are in the specification.")
            : TypedResults.Ok(result);
    }

    private static ValidationProblem Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
