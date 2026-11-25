using System.Security.Claims;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Features.Execution;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Features.Collections;

/// <summary>Sends a saved request through the same executor and history as the ad-hoc request page.</summary>
public static class SendSavedRequestEndpoint
{
    public static void MapSendSavedRequestEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/requests/{id:guid}/execute", Send)
            .WithTags("Requests")
            .RequireAuthorization()
            .RequireRateLimiting(ExecutionEndpoints.RateLimitPolicy)
            .WithSummary("Send a saved request, with variables filled from an environment, and record it in history.")
            .WithDescription(
                "Pass environmentId to resolve {{variables}}, the base URL, shared headers and inherited auth. "
                + "Unresolvable variables return 400. Failures of the target still return 200 with `error` set.");
    }

    private static async Task<Results<Ok<ExecutionResponse>, ValidationProblem, NotFound>> Send(
        Guid id, Guid? environmentId, ClaimsPrincipal user, AppDbContext db, RequestResolver resolver,
        RequestExecutor executor, ExecutionHistory history, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var saved = await db.SavedRequests
            .AsNoTracking()
            .Include(r => r.Collection)
            .SingleOrDefaultAsync(r => r.Id == id && r.Collection.Project.OwnerId == userId, cancellationToken);
        if (saved is null)
        {
            return TypedResults.NotFound();
        }

        var projectId = saved.Collection.ProjectId;
        var environment = environmentId is null
            ? null
            : await db.Environments
                .AsNoTracking()
                .SingleOrDefaultAsync(e => e.Id == environmentId && e.ProjectId == projectId, cancellationToken);
        if (environmentId is not null && environment is null)
        {
            return TypedResults.NotFound();
        }

        var resolved = resolver.Resolve(saved, environment);
        if (resolved.Request is not { } r)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = [.. resolved.Errors],
            });
        }

        var outbound = new OutboundRequest(
            r.Method,
            r.Url,
            [.. r.Headers.Select(h => new HeaderPair(h.Key, h.Value))],
            r.Body,
            r.ContentType,
            r.Auth is null ? null : new RequestAuth(ToAuthKind(r.Auth.Kind), r.Auth.Value, r.Auth.Name),
            r.SecretValues);

        var errors = executor.Validate(outbound);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await executor.ExecuteAsync(outbound, cancellationToken);
        var execution = await history.RecordAsync(projectId, saved.Id, outbound, result, CancellationToken.None);
        return TypedResults.Ok(execution);
    }

    private static AuthKind ToAuthKind(ResolvedAuthKind kind) => kind switch
    {
        ResolvedAuthKind.Bearer => AuthKind.Bearer,
        ResolvedAuthKind.ApiKeyHeader => AuthKind.ApiKeyHeader,
        ResolvedAuthKind.ApiKeyQuery => AuthKind.ApiKeyQuery,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
