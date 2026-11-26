using System.Text.Json.Nodes;

namespace ApiArchitect.Api.Features.Specifications;

/// <summary>What changed between two versions of a document. Endpoints are written as <c>GET /pets/{id}</c>.</summary>
/// <param name="OtherChanges">
/// True when the documents differ in ways not listed here, such as info, servers or tags.
/// </param>
public sealed record SpecificationChanges(
    string OldVersion,
    string NewVersion,
    string[] AddedEndpoints,
    string[] RemovedEndpoints,
    string[] ChangedEndpoints,
    string[] AddedSchemas,
    string[] RemovedSchemas,
    string[] ChangedSchemas,
    bool OtherChanges)
{
    public bool HasChanges =>
        OldVersion != NewVersion || OtherChanges
        || AddedEndpoints.Length + RemovedEndpoints.Length + ChangedEndpoints.Length
           + AddedSchemas.Length + RemovedSchemas.Length + ChangedSchemas.Length > 0;
}

public static class SpecificationComparer
{
    /// <summary>The operation keys a path item can have (OpenAPI 3.2 adds <c>query</c>).</summary>
    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace", "query"];

    /// <summary>Compares two documents that have already passed <see cref="OpenApiImport.Read"/>.</summary>
    public static SpecificationChanges Compare(string oldContent, string oldVersion, string newContent, string newVersion)
    {
        var before = JsonNode.Parse(oldContent);
        var after = JsonNode.Parse(newContent);

        var (addedEndpoints, removedEndpoints, changedEndpoints) = Diff(Operations(before), Operations(after));
        var (addedSchemas, removedSchemas, changedSchemas) = Diff(Schemas(before), Schemas(after));

        // Anything else: compare the documents without paths and schemas.
        var otherChanges = !JsonNode.DeepEquals(WithoutCompared(before), WithoutCompared(after));

        return new SpecificationChanges(oldVersion, newVersion, addedEndpoints, removedEndpoints, changedEndpoints,
            addedSchemas, removedSchemas, changedSchemas, otherChanges);
    }

    private static Dictionary<string, JsonNode?> Operations(JsonNode? document)
    {
        var operations = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (document?["paths"] is not JsonObject paths)
        {
            return operations;
        }

        foreach (var (path, item) in paths)
        {
            if (item is not JsonObject pathItem)
            {
                continue;
            }

            foreach (var method in Methods)
            {
                if (pathItem.TryGetPropertyValue(method, out var operation))
                {
                    operations[$"{method.ToUpperInvariant()} {path}"] = operation;
                }
            }
        }

        return operations;
    }

    private static Dictionary<string, JsonNode?> Schemas(JsonNode? document) =>
        document?["components"]?["schemas"] is JsonObject schemas
            ? schemas.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
            : [];

    private static (string[] Added, string[] Removed, string[] Changed) Diff(
        Dictionary<string, JsonNode?> before, Dictionary<string, JsonNode?> after) =>
    (
        [.. after.Keys.Where(k => !before.ContainsKey(k)).Order(StringComparer.Ordinal)],
        [.. before.Keys.Where(k => !after.ContainsKey(k)).Order(StringComparer.Ordinal)],
        [.. after.Keys.Where(k => before.TryGetValue(k, out var old) && !JsonNode.DeepEquals(old, after[k]))
            .Order(StringComparer.Ordinal)]
    );

    /// <summary>A copy of the document without operations, schemas and info.version, which are reported separately.</summary>
    private static JsonNode? WithoutCompared(JsonNode? document)
    {
        if (document?.DeepClone() is not JsonObject copy)
        {
            return document;
        }

        (copy["info"] as JsonObject)?.Remove("version");
        (copy["components"] as JsonObject)?.Remove("schemas");
        if (copy["paths"] is JsonObject paths)
        {
            foreach (var path in paths.Select(p => p.Key).ToList())
            {
                if (paths[path] is JsonObject pathItem)
                {
                    foreach (var method in Methods)
                    {
                        pathItem.Remove(method);
                    }

                    // A path that only had operations is fully described by the endpoint lists.
                    if (pathItem.Count == 0)
                    {
                        paths.Remove(path);
                    }
                }
            }
        }

        return copy;
    }
}
