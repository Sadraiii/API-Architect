using System.Security.Claims;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Features.Collections;

/// <summary>Saved requests inside a collection: create, edit, move between folders, duplicate, delete.</summary>
public static class SavedRequestEndpoints
{
    public static void MapSavedRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Requests").RequireAuthorization();

        api.MapGet("/collections/{collectionId:guid}/requests", List)
            .WithSummary("List the requests in a collection, in the order they were created.");
        api.MapPost("/collections/{collectionId:guid}/requests", Create).WithSummary("Save a new request.");
        api.MapGet("/requests/{id:guid}", Get).WithName("GetSavedRequest").WithSummary("Get a saved request.");
        api.MapPut("/requests/{id:guid}", Update)
            .WithSummary("Replace a saved request. Changing FolderId moves it to another folder.");
        api.MapPost("/requests/{id:guid}/duplicate", Duplicate)
            .WithSummary("Copy a saved request into the same folder, named \"<name> (copy)\".");
        api.MapDelete("/requests/{id:guid}", Delete).WithSummary("Delete a saved request.");
    }

    private static async Task<Results<Ok<List<RequestSummary>>, NotFound>> List(
        Guid collectionId, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Collections.AnyAsync(c => c.Id == collectionId && c.Project.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var requests = await db.SavedRequests
            .Where(r => r.CollectionId == collectionId)
            .OrderBy(r => r.Id)
            .Select(r => new RequestSummary(r.Id, r.FolderId, r.Name, r.Method, r.UpdatedAt))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(requests);
    }

    private static async Task<Results<Ok<SavedRequestResponse>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var request = await db.SavedRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id && r.Collection.Project.OwnerId == userId, cancellationToken);

        return request is null ? TypedResults.NotFound() : TypedResults.Ok(SavedRequestMapping.ToResponse(request));
    }

    private static async Task<Results<CreatedAtRoute<SavedRequestResponse>, NotFound, ValidationProblem>> Create(
        Guid collectionId, SavedRequestInput input, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Collections.AnyAsync(c => c.Id == collectionId && c.Project.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var errors = await ValidateAsync(input, collectionId, db, cancellationToken);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = clock.GetUtcNowForStorage();
        var request = new SavedRequest
        {
            CollectionId = collectionId,
            FolderId = input.FolderId,
            Name = "",
            Method = "",
            Url = "",
            CreatedAt = now,
            UpdatedAt = now,
        };
        SavedRequestMapping.Apply(input, request);
        db.SavedRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await CollectionEndpoints.TouchCollectionAsync(db, collectionId, now, cancellationToken);

        return TypedResults.CreatedAtRoute(SavedRequestMapping.ToResponse(request), "GetSavedRequest", new { id = request.Id });
    }

    private static async Task<Results<Ok<SavedRequestResponse>, NotFound, ValidationProblem>> Update(
        Guid id, SavedRequestInput input, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var request = await db.SavedRequests
            .SingleOrDefaultAsync(r => r.Id == id && r.Collection.Project.OwnerId == userId, cancellationToken);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        var errors = await ValidateAsync(input, request.CollectionId, db, cancellationToken);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = clock.GetUtcNowForStorage();
        SavedRequestMapping.Apply(input, request);
        request.FolderId = input.FolderId;
        request.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await CollectionEndpoints.TouchCollectionAsync(db, request.CollectionId, now, cancellationToken);

        return TypedResults.Ok(SavedRequestMapping.ToResponse(request));
    }

    private static async Task<Results<CreatedAtRoute<SavedRequestResponse>, NotFound>> Duplicate(
        Guid id, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var source = await db.SavedRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id && r.Collection.Project.OwnerId == userId, cancellationToken);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        const string suffix = " (copy)";
        var baseName = source.Name.Length + suffix.Length > CollectionRules.NameMaxLength
            ? source.Name[..(CollectionRules.NameMaxLength - suffix.Length)]
            : source.Name;

        var now = clock.GetUtcNowForStorage();
        var copy = SavedRequestMapping.Copy(source, baseName + suffix, now);
        db.SavedRequests.Add(copy);
        await db.SaveChangesAsync(cancellationToken);
        await CollectionEndpoints.TouchCollectionAsync(db, copy.CollectionId, now, cancellationToken);

        return TypedResults.CreatedAtRoute(SavedRequestMapping.ToResponse(copy), "GetSavedRequest", new { id = copy.Id });
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var collectionId = await db.SavedRequests
            .Where(r => r.Id == id && r.Collection.Project.OwnerId == userId)
            .Select(r => (Guid?)r.CollectionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (collectionId is null)
        {
            return TypedResults.NotFound();
        }

        await db.SavedRequests.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken);
        await CollectionEndpoints.TouchCollectionAsync(db, collectionId.Value, clock.GetUtcNowForStorage(), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(
        SavedRequestInput input, Guid collectionId, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = SavedRequestMapping.Validate(input);
        if (input.FolderId is { } folderId
            && !await db.Folders.AnyAsync(f => f.Id == folderId && f.CollectionId == collectionId, cancellationToken))
        {
            errors["FolderId"] = ["The folder isn't in this collection."];
        }

        return errors;
    }
}
