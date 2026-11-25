using System.Security.Claims;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Features.Execution;

public static class ExecutionEndpoints
{
    public const string RateLimitPolicy = "execution";

    public static void MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Execution")
            .RequireAuthorization();

        group.MapPost("/projects/{projectId:guid}/execute", Execute)
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Send an HTTP request from the server and record it in the project's history.")
            .WithDescription(
                "Failures of the target (blocked internal address, DNS, connection, TLS, timeout) still return 200, "
                + "with `error` set and no status code. Only invalid input returns 400.");
        group.MapGet("/projects/{projectId:guid}/executions", List)
            .WithSummary("List the project's request history, newest first.");
        group.MapDelete("/projects/{projectId:guid}/executions", Clear)
            .WithSummary("Delete the project's request history.");
        group.MapGet("/executions/{id:guid}", Get)
            .WithSummary("Get one execution from history, with its stored request and response.");
    }

    private static async Task<Results<Ok<ExecutionResponse>, ValidationProblem, NotFound>> Execute(
        Guid projectId, ExecuteRequest body, ClaimsPrincipal user, AppDbContext db, RequestExecutor executor,
        ExecutionHistory history, CancellationToken cancellationToken)
    {
        if (!await OwnsProjectAsync(db, projectId, user.GetUserId(), cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (!Uri.TryCreate(body.Url.Trim(), UriKind.Absolute, out var url))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["url"] = ["URL must be an absolute http:// or https:// address."],
            });
        }

        var request = new OutboundRequest(body.Method, url, body.Headers ?? [], body.Body, body.ContentType, body.Auth);
        var errors = executor.Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await executor.ExecuteAsync(request, cancellationToken);
        // Recorded even when the request failed: "why didn't that work" is what history is often for.
        var execution = await history.RecordAsync(projectId, requestId: null, request, result, CancellationToken.None);
        return TypedResults.Ok(execution);
    }

    private static async Task<Results<Ok<List<ExecutionSummary>>, NotFound>> List(
        Guid projectId, Guid? requestId, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        if (!await OwnsProjectAsync(db, projectId, user.GetUserId(), cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var query = db.RequestExecutions.Where(e => e.ProjectId == projectId);
        if (requestId is not null)
        {
            query = query.Where(e => e.RequestId == requestId);
        }

        var executions = await query
            .OrderByDescending(e => e.ExecutedAt).ThenByDescending(e => e.Id)
            .Select(e => new ExecutionSummary(e.Id, e.RequestId, e.Method, e.Url, e.StatusCode, e.DurationMs, e.ErrorCode, e.ExecutedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(executions);
    }

    private static async Task<Results<Ok<ExecutionResponse>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var execution = await db.RequestExecutions
            .Where(e => e.Id == id && e.Project.OwnerId == userId)
            .Select(ExecutionHistory.ToResponse)
            .SingleOrDefaultAsync(cancellationToken);

        return execution is null ? TypedResults.NotFound() : TypedResults.Ok(execution);
    }

    private static async Task<Results<NoContent, NotFound>> Clear(
        Guid projectId, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        if (!await OwnsProjectAsync(db, projectId, user.GetUserId(), cancellationToken))
        {
            return TypedResults.NotFound();
        }

        await db.RequestExecutions.Where(e => e.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static Task<bool> OwnsProjectAsync(AppDbContext db, Guid projectId, string userId, CancellationToken cancellationToken) =>
        db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken);
}
