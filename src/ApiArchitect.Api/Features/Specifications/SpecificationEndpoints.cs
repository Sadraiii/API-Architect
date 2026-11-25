using System.Linq.Expressions;
using System.Security.Claims;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Features.Specifications;

public static class SpecificationEndpoints
{
    public static void MapSpecificationEndpoints(this IEndpointRouteBuilder app)
    {
        var bodyLimit = new RequestSizeLimitAttribute(SpecificationRules.RequestBodyMaxBytes);

        var byProject = app.MapGroup("/api/projects/{projectId:guid}/specifications")
            .WithTags("Specifications")
            .RequireAuthorization();

        byProject.MapGet("/", List).WithSummary("List a project's imported OpenAPI specifications, most recently updated first.");
        byProject.MapPost("/", Import)
            .WithMetadata(bodyLimit)
            .WithSummary("Import an OpenAPI 3.x JSON document into a project.");

        var single = app.MapGroup("/api/specifications")
            .WithTags("Specifications")
            .RequireAuthorization();

        single.MapGet("/{id:guid}", Get).WithName("GetSpecification").WithSummary("Get a specification's details.");
        single.MapGet("/{id:guid}/document", GetDocument)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .WithSummary("Export the OpenAPI document exactly as it was imported.");
        single.MapPut("/{id:guid}", Replace)
            .WithMetadata(bodyLimit)
            .WithSummary("Rename a specification and replace its document with a new version.");
        single.MapDelete("/{id:guid}", Delete).WithSummary("Delete a specification.");
    }

    // Ownership: a specification belongs to whoever owns its project. Every query filters on
    // Project.OwnerId, and anything that isn't yours is a 404.

    private static async Task<Results<Ok<List<SpecificationResponse>>, NotFound>> List(
        Guid projectId, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var specifications = await db.ApiSpecifications
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(ToResponse)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(specifications);
    }

    private static async Task<Results<CreatedAtRoute<SpecificationResponse>, ValidationProblem, NotFound>> Import(
        Guid projectId, SpecificationRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken);
        if (project is null)
        {
            return TypedResults.NotFound();
        }

        var (document, errors) = OpenApiImport.Read(request.Content);
        if (document is null)
        {
            return ContentProblem(errors);
        }

        var now = clock.GetUtcNowForStorage();
        var specification = new ApiSpecification
        {
            ProjectId = project.Id,
            Name = request.Name.Trim(),
            Content = request.Content,
            Title = document.Title,
            Version = document.Version,
            OpenApiVersion = document.OpenApiVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };
        project.UpdatedAt = now;

        db.ApiSpecifications.Add(specification);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.CreatedAtRoute(MapToResponse(specification), "GetSpecification", new { id = specification.Id });
    }

    private static async Task<Results<Ok<SpecificationResponse>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var specification = await db.ApiSpecifications
            .Where(s => s.Id == id && s.Project.OwnerId == userId)
            .Select(ToResponse)
            .SingleOrDefaultAsync(cancellationToken);

        return specification is null ? TypedResults.NotFound() : TypedResults.Ok(specification);
    }

    private static async Task<Results<ContentHttpResult, NotFound>> GetDocument(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var content = await db.ApiSpecifications
            .Where(s => s.Id == id && s.Project.OwnerId == userId)
            .Select(s => s.Content)
            .SingleOrDefaultAsync(cancellationToken);

        return content is null
            ? TypedResults.NotFound()
            : TypedResults.Text(content, "application/json; charset=utf-8");
    }

    private static async Task<Results<Ok<SpecificationResponse>, ValidationProblem, NotFound>> Replace(
        Guid id, SpecificationRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var specification = await db.ApiSpecifications
            .Include(s => s.Project)
            .SingleOrDefaultAsync(s => s.Id == id && s.Project.OwnerId == userId, cancellationToken);
        if (specification is null)
        {
            return TypedResults.NotFound();
        }

        var (document, errors) = OpenApiImport.Read(request.Content);
        if (document is null)
        {
            return ContentProblem(errors);
        }

        var now = clock.GetUtcNowForStorage();
        specification.Name = request.Name.Trim();
        specification.Content = request.Content;
        specification.Title = document.Title;
        specification.Version = document.Version;
        specification.OpenApiVersion = document.OpenApiVersion;
        specification.UpdatedAt = now;
        specification.Project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(MapToResponse(specification));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var deleted = await db.ApiSpecifications
            .Where(s => s.Id == id && s.Project.OwnerId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>Reports document problems the same way DataAnnotations failures are reported: a 400 keyed by field.</summary>
    private static ValidationProblem ContentProblem(string[] errors) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Content"] = errors });

    private static readonly Expression<Func<ApiSpecification, SpecificationResponse>> ToResponse = s =>
        new SpecificationResponse(s.Id, s.ProjectId, s.Name, s.Title, s.Version, s.OpenApiVersion, s.CreatedAt, s.UpdatedAt);

    // The same mapping, for entities already in memory. Declared after ToResponse so it initializes second.
    private static readonly Func<ApiSpecification, SpecificationResponse> MapToResponse = ToResponse.Compile();
}
