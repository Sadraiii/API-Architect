using System.Linq.Expressions;
using System.Security.Claims;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Features.Execution;
using ApiArchitect.Api.Features.Projects;
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
        // These make the server download a URL, so they share the request runner's per-user rate limit.
        byProject.MapPost("/from-url", ImportFromUrl)
            .RequireRateLimiting(ExecutionEndpoints.RateLimitPolicy)
            .WithSummary("Download an OpenAPI 3.x JSON document from a URL and import it into a project.");

        app.MapPost("/api/projects/import", CreateProject)
            .WithTags("Specifications")
            .RequireAuthorization()
            .RequireRateLimiting(ExecutionEndpoints.RateLimitPolicy)
            .WithMetadata(bodyLimit)
            .WithSummary("Create a new project from an OpenAPI document, given as JSON text or a URL. The project is named after info.title.");

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
        single.MapPost("/{id:guid}/reimport", Reimport)
            .RequireRateLimiting(ExecutionEndpoints.RateLimitPolicy)
            .WithSummary("Download the document again from the URL it was imported from, replace it, and list what changed.");
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
            return Problem("Content", errors);
        }

        var specification = AddSpecification(db, project, request.Name.Trim(), request.Content, document, null, clock);
        await db.SaveChangesAsync(cancellationToken);

        return Created(specification);
    }

    private static async Task<Results<CreatedAtRoute<SpecificationResponse>, ValidationProblem, NotFound>> ImportFromUrl(
        Guid projectId, SpecificationUrlRequest request, ClaimsPrincipal user, AppDbContext db,
        SpecificationFetcher fetcher, TimeProvider clock, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken);
        if (project is null)
        {
            return TypedResults.NotFound();
        }

        var (content, fetchError) = await fetcher.FetchAsync(request.Url, cancellationToken);
        if (content is null)
        {
            return Problem("Url", fetchError!);
        }

        var (document, errors) = OpenApiImport.Read(content);
        if (document is null)
        {
            return Problem("Content", errors);
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? NameFrom(document.Title) : request.Name.Trim();
        var specification = AddSpecification(db, project, name, content, document, request.Url.Trim(), clock);
        await db.SaveChangesAsync(cancellationToken);

        return Created(specification);
    }

    /// <summary>One step from a document to a project of its own: the project and the specification are named after info.title.</summary>
    private static async Task<Results<CreatedAtRoute<SpecificationResponse>, ValidationProblem>> CreateProject(
        ProjectFromSpecificationRequest request, ClaimsPrincipal user, AppDbContext db,
        SpecificationFetcher fetcher, TimeProvider clock, CancellationToken cancellationToken)
    {
        var hasContent = !string.IsNullOrWhiteSpace(request.Content);
        var hasUrl = !string.IsNullOrWhiteSpace(request.Url);
        if (hasContent == hasUrl)
        {
            return Problem("Content", "Send either the document's content or a URL to download it from.");
        }

        var content = request.Content;
        if (hasUrl)
        {
            (content, var fetchError) = await fetcher.FetchAsync(request.Url!, cancellationToken);
            if (content is null)
            {
                return Problem("Url", fetchError!);
            }
        }

        var (document, errors) = OpenApiImport.Read(content!);
        if (document is null)
        {
            return Problem("Content", errors);
        }

        var now = clock.GetUtcNowForStorage();
        var project = new Project
        {
            OwnerId = user.GetUserId(),
            Name = document.Title.Length > ProjectRules.NameMaxLength ? document.Title[..ProjectRules.NameMaxLength] : document.Title,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);

        var specification = AddSpecification(db, project, NameFrom(document.Title), content!, document,
            hasUrl ? request.Url!.Trim() : null, clock);
        await db.SaveChangesAsync(cancellationToken);

        return Created(specification);
    }

    private static async Task<Results<Ok<ReimportResponse>, ValidationProblem, NotFound>> Reimport(
        Guid id, ClaimsPrincipal user, AppDbContext db, SpecificationFetcher fetcher, TimeProvider clock,
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

        if (specification.SourceUrl is null)
        {
            return Problem("Url", "This specification wasn't imported from a URL. Upload a new version instead.");
        }

        var (content, fetchError) = await fetcher.FetchAsync(specification.SourceUrl, cancellationToken);
        if (content is null)
        {
            return Problem("Url", fetchError!);
        }

        var (document, errors) = OpenApiImport.Read(content);
        if (document is null)
        {
            return Problem("Content", errors);
        }

        var changes = SpecificationComparer.Compare(specification.Content, specification.Version, content, document.Version);

        // Nothing to save when the document is byte-for-byte the same; UpdatedAt then still means "last real change".
        if (content != specification.Content)
        {
            var now = clock.GetUtcNowForStorage();
            specification.Content = content;
            specification.Title = document.Title;
            specification.Version = document.Version;
            specification.OpenApiVersion = document.OpenApiVersion;
            specification.UpdatedAt = now;
            specification.Project.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new ReimportResponse(MapToResponse(specification), changes));
    }

    private static ApiSpecification AddSpecification(
        AppDbContext db, Project project, string name, string content, OpenApiImport.Result document, string? sourceUrl,
        TimeProvider clock)
    {
        var now = clock.GetUtcNowForStorage();
        var specification = new ApiSpecification
        {
            ProjectId = project.Id,
            Name = name,
            Content = content,
            Title = document.Title,
            Version = document.Version,
            OpenApiVersion = document.OpenApiVersion,
            SourceUrl = sourceUrl,
            CreatedAt = now,
            UpdatedAt = now,
        };
        project.UpdatedAt = now;
        db.ApiSpecifications.Add(specification);
        return specification;
    }

    private static CreatedAtRoute<SpecificationResponse> Created(ApiSpecification specification) =>
        TypedResults.CreatedAtRoute(MapToResponse(specification), "GetSpecification", new { id = specification.Id });

    /// <summary>info.title can be up to 200 characters; names are up to 100.</summary>
    private static string NameFrom(string title) =>
        title.Length > SpecificationRules.NameMaxLength ? title[..SpecificationRules.NameMaxLength] : title;

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
            return Problem("Content", errors);
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

    /// <summary>Reports problems the same way DataAnnotations failures are reported: a 400 keyed by field.</summary>
    private static ValidationProblem Problem(string field, params string[] errors) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = errors });

    private static readonly Expression<Func<ApiSpecification, SpecificationResponse>> ToResponse = s =>
        new SpecificationResponse(s.Id, s.ProjectId, s.Name, s.Title, s.Version, s.OpenApiVersion, s.CreatedAt, s.UpdatedAt, s.SourceUrl);

    // The same mapping, for entities already in memory. Declared after ToResponse so it initializes second.
    private static readonly Func<ApiSpecification, SpecificationResponse> MapToResponse = ToResponse.Compile();
}
