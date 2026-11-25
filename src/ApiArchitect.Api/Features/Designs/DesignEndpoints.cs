using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Features.Specifications;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Features.Designs;

public static class DesignEndpoints
{
    /// <summary>How the design document is stored: the same camelCase JSON the API sends and receives.</summary>
    private static readonly JsonSerializerOptions StorageOptions = JsonSerializerOptions.Web;

    public static void MapDesignEndpoints(this IEndpointRouteBuilder app)
    {
        var byProject = app.MapGroup("/api/projects/{projectId:guid}/designs")
            .WithTags("Designs")
            .RequireAuthorization();

        byProject.MapGet("/", List).WithSummary("List a project's API designs, most recently updated first.");
        byProject.MapPost("/", Create).WithSummary("Create an API design, blank or from the Bookstore example.");

        var single = app.MapGroup("/api/designs")
            .WithTags("Designs")
            .RequireAuthorization();

        single.MapGet("/{id:guid}", Get).WithName("GetDesign").WithSummary("Get a design with its models and endpoints.");
        single.MapPut("/{id:guid}", Update)
            .WithMetadata(new RequestSizeLimitAttribute(DesignRules.RequestBodyMaxBytes))
            .WithSummary("Save a design. The whole document is validated and replaces the stored one.");
        single.MapDelete("/{id:guid}", Delete).WithSummary("Delete a design. A specification saved from it is kept.");
        single.MapGet("/{id:guid}/openapi", GetOpenApi)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .WithSummary("The OpenAPI 3.1 document for the saved design.");
        single.MapGet("/{id:guid}/code", GetCode).WithSummary("The files of the generated ASP.NET Core project, for previewing.");
        single.MapGet("/{id:guid}/code/zip", DownloadCode)
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .WithSummary("Download the generated ASP.NET Core project as a zip.");
        single.MapPost("/{id:guid}/specification", SaveAsSpecification)
            .WithSummary("Save the design's OpenAPI document as a project specification, or update the one saved before.");
    }

    // Ownership: a design belongs to whoever owns its project. Every query filters on Project.OwnerId,
    // and anything that isn't yours is a 404.

    private static async Task<Results<Ok<List<DesignSummaryResponse>>, NotFound>> List(
        Guid projectId, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var designs = await db.ApiDesigns
            .Where(d => d.ProjectId == projectId)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(designs);
    }

    private static async Task<Results<CreatedAtRoute<DesignResponse>, NotFound>> Create(
        Guid projectId, CreateDesignRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken);
        if (project is null)
        {
            return TypedResults.NotFound();
        }

        var name = request.Name.Trim();
        var document = request.StartFrom == "example" ? DesignExamples.Bookstore() : DesignExamples.Blank(name);

        var now = clock.GetUtcNowForStorage();
        var design = new ApiDesign
        {
            ProjectId = project.Id,
            Name = name,
            Content = JsonSerializer.Serialize(document, StorageOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };
        project.UpdatedAt = now;

        db.ApiDesigns.Add(design);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.CreatedAtRoute(ToResponse(design, document), "GetDesign", new { id = design.Id });
    }

    private static async Task<Results<Ok<DesignResponse>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var design = await FindAsync(db, id, user, cancellationToken);
        return design is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(design, Read(design)));
    }

    private static async Task<Results<Ok<DesignResponse>, ValidationProblem, NotFound>> Update(
        Guid id, UpdateDesignRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var design = await db.ApiDesigns
            .Include(d => d.Project)
            .SingleOrDefaultAsync(d => d.Id == id && d.Project.OwnerId == userId, cancellationToken);
        if (design is null)
        {
            return TypedResults.NotFound();
        }

        var errors = DesignValidator.Validate(request.Document);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = clock.GetUtcNowForStorage();
        design.Name = request.Name.Trim();
        design.Content = JsonSerializer.Serialize(request.Document, StorageOptions);
        design.UpdatedAt = now;
        design.Project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(design, request.Document));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var deleted = await db.ApiDesigns
            .Where(d => d.Id == id && d.Project.OwnerId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Results<ContentHttpResult, ValidationProblem, NotFound>> GetOpenApi(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var design = await FindAsync(db, id, user, cancellationToken);
        if (design is null)
        {
            return TypedResults.NotFound();
        }

        var (document, errors) = ReadValid(design);
        return document is null
            ? TypedResults.ValidationProblem(errors)
            : TypedResults.Text(DesignOpenApiWriter.Write(document), "application/json; charset=utf-8");
    }

    private static async Task<Results<Ok<IReadOnlyList<CSharpProjectGenerator.GeneratedFile>>, ValidationProblem, NotFound>> GetCode(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var design = await FindAsync(db, id, user, cancellationToken);
        if (design is null)
        {
            return TypedResults.NotFound();
        }

        var (document, errors) = ReadValid(design);
        return document is null
            ? TypedResults.ValidationProblem(errors)
            : TypedResults.Ok(CSharpProjectGenerator.Generate(document));
    }

    private static async Task<Results<FileContentHttpResult, ValidationProblem, NotFound>> DownloadCode(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var design = await FindAsync(db, id, user, cancellationToken);
        if (design is null)
        {
            return TypedResults.NotFound();
        }

        var (document, errors) = ReadValid(design);
        if (document is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var zip = CSharpProjectGenerator.Zip(CSharpProjectGenerator.Generate(document));
        return TypedResults.File(zip, "application/zip", $"{document.Namespace}.zip");
    }

    private static async Task<Results<Ok<SpecificationResponse>, ValidationProblem, NotFound>> SaveAsSpecification(
        Guid id, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var design = await db.ApiDesigns
            .Include(d => d.Project)
            .Include(d => d.Specification)
            .SingleOrDefaultAsync(d => d.Id == id && d.Project.OwnerId == userId, cancellationToken);
        if (design is null)
        {
            return TypedResults.NotFound();
        }

        var (document, errors) = ReadValid(design);
        if (document is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var content = DesignOpenApiWriter.Write(document);

        // The same check an import goes through. A design that passed DesignValidator always passes this;
        // if it ever doesn't, that's a bug in the writer, and saving a broken specification would hide it.
        var (parsed, problems) = OpenApiImport.Read(content);
        if (parsed is null)
        {
            throw new InvalidOperationException($"The generated OpenAPI document didn't parse: {string.Join("; ", problems)}");
        }

        var now = clock.GetUtcNowForStorage();
        var specification = design.Specification;
        if (specification is null)
        {
            specification = new ApiSpecification
            {
                ProjectId = design.ProjectId,
                Name = design.Name,
                Content = content,
                Title = parsed.Title,
                Version = parsed.Version,
                OpenApiVersion = parsed.OpenApiVersion,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ApiSpecifications.Add(specification);
            design.Specification = specification;
        }
        else
        {
            specification.Content = content;
            specification.Title = parsed.Title;
            specification.Version = parsed.Version;
            specification.OpenApiVersion = parsed.OpenApiVersion;
            specification.UpdatedAt = now;
        }

        design.Project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new SpecificationResponse(
            specification.Id, specification.ProjectId, specification.Name, specification.Title, specification.Version,
            specification.OpenApiVersion, specification.CreatedAt, specification.UpdatedAt));
    }

    private static Task<ApiDesign?> FindAsync(AppDbContext db, Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        return db.ApiDesigns.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.Project.OwnerId == userId, cancellationToken);
    }

    private static DesignDocument Read(ApiDesign design) =>
        JsonSerializer.Deserialize<DesignDocument>(design.Content, StorageOptions)
            ?? throw new InvalidOperationException($"Design {design.Id} has no content.");

    /// <summary>
    /// The stored document, checked again before generating anything: designs saved under older, looser rules
    /// get a clear 400 instead of broken output.
    /// </summary>
    private static (DesignDocument? Document, Dictionary<string, string[]> Errors) ReadValid(ApiDesign design)
    {
        var document = Read(design);
        var errors = DesignValidator.Validate(document);
        return errors.Count > 0 ? (null, errors) : (document, errors);
    }

    private static DesignResponse ToResponse(ApiDesign design, DesignDocument document) =>
        new(design.Id, design.ProjectId, design.Name, document, design.SpecificationId, design.CreatedAt, design.UpdatedAt);

    private static readonly Expression<Func<ApiDesign, DesignSummaryResponse>> ToSummary = d =>
        new DesignSummaryResponse(d.Id, d.ProjectId, d.Name, d.SpecificationId, d.CreatedAt, d.UpdatedAt);
}
