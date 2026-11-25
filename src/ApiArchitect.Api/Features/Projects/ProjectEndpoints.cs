using System.Linq.Expressions;
using System.Security.Claims;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Features.Projects;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        group.MapGet("/", List).WithSummary("List your projects, most recently updated first.");
        group.MapPost("/", Create).WithSummary("Create a project.");
        group.MapGet("/{id:guid}", Get).WithName("GetProject").WithSummary("Get one of your projects.");
        group.MapPut("/{id:guid}", Update).WithSummary("Replace a project's details.");
        group.MapDelete("/{id:guid}", Delete).WithSummary("Delete a project and everything in it.");
    }

    // Every query below filters on OwnerId. Another user's project is reported as 404, not 403,
    // so the API never confirms that an id exists.

    private static async Task<Ok<List<ProjectResponse>>> List(
        ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var projects = await db.Projects
            .Where(p => p.OwnerId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(ToResponse)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(projects);
    }

    private static async Task<Results<Ok<ProjectResponse>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var project = await db.Projects
            .Where(p => p.Id == id && p.OwnerId == userId)
            .Select(ToResponse)
            .SingleOrDefaultAsync(cancellationToken);

        return project is null ? TypedResults.NotFound() : TypedResults.Ok(project);
    }

    private static async Task<CreatedAtRoute<ProjectResponse>> Create(
        ProjectRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNowForStorage();
        var project = new Project
        {
            OwnerId = user.GetUserId(),
            Name = request.Name.Trim(),
            Description = NullIfBlank(request.Description),
            TargetFramework = request.TargetFramework,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.CreatedAtRoute(MapToResponse(project), "GetProject", new { id = project.Id });
    }

    private static async Task<Results<Ok<ProjectResponse>, NotFound>> Update(
        Guid id, ProjectRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == id && p.OwnerId == userId, cancellationToken);
        if (project is null)
        {
            return TypedResults.NotFound();
        }

        project.Name = request.Name.Trim();
        project.Description = NullIfBlank(request.Description);
        project.TargetFramework = request.TargetFramework;
        project.UpdatedAt = clock.GetUtcNowForStorage();
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(MapToResponse(project));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var deleted = await db.Projects
            .Where(p => p.Id == id && p.OwnerId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static readonly Expression<Func<Project, ProjectResponse>> ToResponse = p =>
        new ProjectResponse(p.Id, p.Name, p.Description, p.TargetFramework, p.CreatedAt, p.UpdatedAt);

    // The same mapping, for entities already in memory. Declared after ToResponse so it initializes second.
    private static readonly Func<Project, ProjectResponse> MapToResponse = ToResponse.Compile();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
