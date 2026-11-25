using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Features.Collections;

/// <summary>Collections, their folders, and Postman import/export.</summary>
public static class CollectionEndpoints
{
    /// <summary>Largest Postman file accepted for import.</summary>
    internal const long MaxImportBytes = 10 * 1024 * 1024;

    public static void MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Collections").RequireAuthorization();

        api.MapGet("/projects/{projectId:guid}/collections", List)
            .WithSummary("List a project's request collections.");
        api.MapPost("/projects/{projectId:guid}/collections", Create)
            .WithSummary("Create an empty collection.");
        api.MapPost("/projects/{projectId:guid}/collections/import", Import)
            .Accepts<JsonElement>("application/json")
            .WithSummary("Import a Postman collection (format v2.0 or v2.1) as a new collection.");
        api.MapGet("/collections/{id:guid}", Get)
            .WithName("GetCollection")
            .WithSummary("Get a collection with all its folders and requests.");
        api.MapPut("/collections/{id:guid}", Update).WithSummary("Rename a collection or change its description.");
        api.MapDelete("/collections/{id:guid}", Delete).WithSummary("Delete a collection and everything in it.");
        api.MapGet("/collections/{id:guid}/export", Export)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .WithSummary("Download a collection as a Postman v2.1 collection file.");

        api.MapPost("/collections/{collectionId:guid}/folders", CreateFolder).WithSummary("Create a folder.");
        api.MapPut("/folders/{id:guid}", UpdateFolder).WithSummary("Rename a folder or move it to another parent.");
        api.MapDelete("/folders/{id:guid}", DeleteFolder)
            .WithSummary("Delete a folder with all its subfolders and requests.");
    }

    // Ownership: every query reaches the owning project through the navigation chain and filters on
    // its OwnerId. Anything owned by someone else is a 404, so ids are never confirmed.

    private static async Task<Results<Ok<List<CollectionSummary>>, NotFound>> List(
        Guid projectId, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var collections = await db.Collections
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.Name)
            .Select(c => new CollectionSummary(
                c.Id, c.ProjectId, c.Name, c.Description,
                db.SavedRequests.Count(r => r.CollectionId == c.Id),
                c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(collections);
    }

    private static async Task<Results<CreatedAtRoute<CollectionSummary>, NotFound>> Create(
        Guid projectId, CollectionInput input, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNowForStorage();
        var collection = new RequestCollection
        {
            ProjectId = projectId,
            Name = input.Name.Trim(),
            Description = NullIfBlank(input.Description),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.CreatedAtRoute(ToSummary(collection, 0), "GetCollection", new { id = collection.Id });
    }

    private static async Task<Results<Ok<CollectionDetail>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var collection = await db.Collections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.Project.OwnerId == userId, cancellationToken);
        if (collection is null)
        {
            return TypedResults.NotFound();
        }

        var folders = await db.Folders
            .Where(f => f.CollectionId == id)
            .OrderBy(f => f.Id)
            .Select(f => new FolderResponse(f.Id, f.Name, f.ParentFolderId))
            .ToListAsync(cancellationToken);
        var requests = await db.SavedRequests
            .Where(r => r.CollectionId == id)
            .OrderBy(r => r.Id)
            .Select(r => new RequestSummary(r.Id, r.FolderId, r.Name, r.Method, r.UpdatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new CollectionDetail(
            collection.Id, collection.ProjectId, collection.Name, collection.Description,
            collection.CreatedAt, collection.UpdatedAt, folders, requests));
    }

    private static async Task<Results<Ok<CollectionSummary>, NotFound>> Update(
        Guid id, CollectionInput input, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var collection = await db.Collections
            .SingleOrDefaultAsync(c => c.Id == id && c.Project.OwnerId == userId, cancellationToken);
        if (collection is null)
        {
            return TypedResults.NotFound();
        }

        collection.Name = input.Name.Trim();
        collection.Description = NullIfBlank(input.Description);
        collection.UpdatedAt = clock.GetUtcNowForStorage();
        await db.SaveChangesAsync(cancellationToken);

        var requestCount = await db.SavedRequests.CountAsync(r => r.CollectionId == id, cancellationToken);
        return TypedResults.Ok(ToSummary(collection, requestCount));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var deleted = await db.Collections
            .Where(c => c.Id == id && c.Project.OwnerId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Results<Created<FolderResponse>, NotFound, ValidationProblem>> CreateFolder(
        Guid collectionId, FolderInput input, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Collections.AnyAsync(c => c.Id == collectionId && c.Project.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var tree = await LoadFolderTreeAsync(db, collectionId, cancellationToken);
        if (tree.CheckPlacement(null, input.ParentFolderId) is { } problem)
        {
            return ParentFolderProblem(problem);
        }

        var now = clock.GetUtcNowForStorage();
        var folder = new RequestFolder
        {
            CollectionId = collectionId,
            ParentFolderId = input.ParentFolderId,
            Name = input.Name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
        await TouchCollectionAsync(db, collectionId, now, cancellationToken);

        return TypedResults.Created(
            $"/api/collections/{collectionId}", new FolderResponse(folder.Id, folder.Name, folder.ParentFolderId));
    }

    private static async Task<Results<Ok<FolderResponse>, NotFound, ValidationProblem>> UpdateFolder(
        Guid id, FolderInput input, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var folder = await db.Folders
            .SingleOrDefaultAsync(f => f.Id == id && f.Collection.Project.OwnerId == userId, cancellationToken);
        if (folder is null)
        {
            return TypedResults.NotFound();
        }

        var tree = await LoadFolderTreeAsync(db, folder.CollectionId, cancellationToken);
        if (tree.CheckPlacement(folder.Id, input.ParentFolderId) is { } problem)
        {
            return ParentFolderProblem(problem);
        }

        var now = clock.GetUtcNowForStorage();
        folder.Name = input.Name.Trim();
        folder.ParentFolderId = input.ParentFolderId;
        folder.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await TouchCollectionAsync(db, folder.CollectionId, now, cancellationToken);

        return TypedResults.Ok(new FolderResponse(folder.Id, folder.Name, folder.ParentFolderId));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteFolder(
        Guid id, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var collectionId = await db.Folders
            .Where(f => f.Id == id && f.Collection.Project.OwnerId == userId)
            .Select(f => (Guid?)f.CollectionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (collectionId is null)
        {
            return TypedResults.NotFound();
        }

        // Subfolders and requests inside go with it through the database's cascading foreign keys.
        await db.Folders.Where(f => f.Id == id).ExecuteDeleteAsync(cancellationToken);
        await TouchCollectionAsync(db, collectionId.Value, clock.GetUtcNowForStorage(), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<CollectionImportResponse>, NotFound, ProblemHttpResult>> Import(
        Guid projectId, HttpRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        // The body is read by hand so this endpoint can have its own size limit, below the server default.
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
        {
            sizeLimit.MaxRequestBodySize = MaxImportBytes;
        }

        if (request.ContentLength > MaxImportBytes)
        {
            return ImportProblem("The file is larger than 10 MB.", StatusCodes.Status413PayloadTooLarge);
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return ImportProblem("The file isn't valid JSON.");
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return ImportProblem("The file is larger than 10 MB.", StatusCodes.Status413PayloadTooLarge);
        }

        PostmanImport import;
        using (document)
        {
            var parsed = PostmanCollectionFormat.Parse(document.RootElement);
            if (parsed.Error is not null)
            {
                return ImportProblem(parsed.Error);
            }

            import = parsed.Import!;
        }

        var now = clock.GetUtcNowForStorage();
        var collection = new RequestCollection
        {
            ProjectId = projectId,
            Name = import.Name,
            Description = import.Description,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Collections.Add(collection);
        AddImportedItems(db, collection.Id, parentFolderId: null, import.Items, now);
        await db.SaveChangesAsync(cancellationToken);

        var summary = ToSummary(collection, import.RequestCount);
        return TypedResults.Created($"/api/collections/{collection.Id}", new CollectionImportResponse(summary, import.Warnings));
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> Export(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var collection = await db.Collections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.Project.OwnerId == userId, cancellationToken);
        if (collection is null)
        {
            return TypedResults.NotFound();
        }

        var folders = await db.Folders.AsNoTracking().Where(f => f.CollectionId == id).OrderBy(f => f.Id)
            .ToListAsync(cancellationToken);
        var requests = await db.SavedRequests.AsNoTracking().Where(r => r.CollectionId == id).OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var json = PostmanCollectionFormat.Export(collection, folders, requests);
        var bytes = Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return TypedResults.File(bytes, "application/json", $"{SafeFileName(collection.Name)}.postman_collection.json");
    }

    private static void AddImportedItems(
        AppDbContext db, Guid collectionId, Guid? parentFolderId, List<PostmanItem> items, DateTimeOffset now)
    {
        foreach (var item in items)
        {
            if (item.Request is { } request)
            {
                db.SavedRequests.Add(new SavedRequest
                {
                    CollectionId = collectionId,
                    FolderId = parentFolderId,
                    Name = request.Name,
                    Method = request.Method,
                    Url = request.Url,
                    Headers = request.Headers,
                    QueryParameters = request.QueryParameters,
                    Body = request.Body,
                    ContentType = request.ContentType,
                    Auth = request.Auth,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                var folder = new RequestFolder
                {
                    CollectionId = collectionId,
                    ParentFolderId = parentFolderId,
                    Name = item.Name,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Folders.Add(folder);
                AddImportedItems(db, collectionId, folder.Id, item.Children, now);
            }
        }
    }

    internal static async Task<FolderTree> LoadFolderTreeAsync(
        AppDbContext db, Guid collectionId, CancellationToken cancellationToken)
    {
        var parents = await db.Folders
            .Where(f => f.CollectionId == collectionId)
            .ToDictionaryAsync(f => f.Id, f => f.ParentFolderId, cancellationToken);
        return new FolderTree(parents);
    }

    /// <summary>Bumps the collection's UpdatedAt so "recently changed" lists reflect edits inside it.</summary>
    internal static Task TouchCollectionAsync(
        AppDbContext db, Guid collectionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Collections
            .Where(c => c.Id == collectionId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, now), cancellationToken);

    private static ValidationProblem ParentFolderProblem(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["ParentFolderId"] = [message] });

    private static ProblemHttpResult ImportProblem(string detail, int status = StatusCodes.Status400BadRequest) =>
        TypedResults.Problem(title: "The collection couldn't be imported.", detail: detail, statusCode: status);

    private static CollectionSummary ToSummary(RequestCollection c, int requestCount) =>
        new(c.Id, c.ProjectId, c.Name, c.Description, requestCount, c.CreatedAt, c.UpdatedAt);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SafeFileName(string name)
    {
        var safe = new string(name.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray()).Trim('-');
        return safe.Length == 0 ? "collection" : safe;
    }
}
