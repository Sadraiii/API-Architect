using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiArchitect.Api.Features.Collections;

/// <summary>A folder (with <see cref="Children"/>) or a request, read from a Postman file.</summary>
internal sealed record PostmanItem(string Name, PostmanRequest? Request, List<PostmanItem> Children);

internal sealed record PostmanRequest(
    string Name,
    string Method,
    string Url,
    List<KeyValueEntry> Headers,
    List<KeyValueEntry> QueryParameters,
    string? Body,
    string? ContentType,
    AuthSettings Auth);

internal sealed record PostmanImport(
    string Name, string? Description, List<PostmanItem> Items, int RequestCount, List<string> Warnings);

internal sealed record PostmanParseResult(PostmanImport? Import, string? Error);

/// <summary>
/// Reads and writes Postman collections, format v2.0 and v2.1 (https://schema.postman.com/).
/// Postman's <c>{{variable}}</c> syntax is the same as ours, so variables carry over unchanged.
/// Anything without an equivalent here (scripts, form-data bodies, OAuth, collection variables) is
/// skipped and reported as a warning instead of failing the whole import.
/// </summary>
internal static class PostmanCollectionFormat
{
    public const string SchemaV21 = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";

    /// <summary>Upper bounds on what one import may create, so one file can't flood the database.</summary>
    public const int MaxImportedRequests = 2000;
    public const int MaxImportedFolders = 500;

    public static PostmanParseResult Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("item", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return new(null, "This doesn't look like a Postman collection: it needs \"info\" and \"item\".");
        }

        var schema = GetString(info, "schema") ?? "";
        if (!schema.Contains("/v2.0.0/", StringComparison.Ordinal) && !schema.Contains("/v2.1.0/", StringComparison.Ordinal))
        {
            return new(null, "Only Postman collection format v2.0 and v2.1 can be imported. Export the collection from Postman as v2.1.");
        }

        var reader = new Reader();
        var name = Limit(GetString(info, "name"), CollectionRules.NameMaxLength) ?? "Imported collection";
        var description = Limit(GetDescription(info), CollectionRules.DescriptionMaxLength);

        if (root.TryGetProperty("variable", out var variables) && variables.ValueKind == JsonValueKind.Array
            && variables.GetArrayLength() > 0)
        {
            reader.Warnings.Add("Collection variables weren't imported. Add them to an environment instead.");
        }

        if (root.TryGetProperty("auth", out var collectionAuth) && collectionAuth.ValueKind == JsonValueKind.Object)
        {
            reader.Warnings.Add("Collection-level auth wasn't imported. Set it on an environment, which every request inherits by default.");
        }

        if (root.TryGetProperty("event", out _))
        {
            reader.Warnings.Add("Pre-request and test scripts aren't supported and were skipped.");
        }

        var tree = reader.ReadItems(items, depth: 1, path: "");
        if (reader.TooLarge)
        {
            return new(null, $"The collection is too large. The limit is {MaxImportedRequests} requests and {MaxImportedFolders} folders.");
        }

        return new(new PostmanImport(name, description, tree, reader.RequestCount, reader.Warnings), null);
    }

    public static JsonObject Export(RequestCollection collection, List<RequestFolder> folders, List<SavedRequest> requests)
    {
        JsonArray ItemsIn(Guid? folderId)
        {
            var array = new JsonArray();
            foreach (var folder in folders.Where(f => f.ParentFolderId == folderId))
            {
                array.Add(new JsonObject { ["name"] = folder.Name, ["item"] = ItemsIn(folder.Id) });
            }

            foreach (var request in requests.Where(r => r.FolderId == folderId))
            {
                array.Add(ExportRequest(request));
            }

            return array;
        }

        var info = new JsonObject { ["name"] = collection.Name, ["schema"] = SchemaV21 };
        if (collection.Description is not null)
        {
            info["description"] = collection.Description;
        }

        return new JsonObject { ["info"] = info, ["item"] = ItemsIn(null) };
    }

    private static JsonObject ExportRequest(SavedRequest request)
    {
        var headers = new JsonArray();
        foreach (var header in request.Headers)
        {
            var entry = new JsonObject { ["key"] = header.Name, ["value"] = header.Value };
            if (!header.Enabled)
            {
                entry["disabled"] = true;
            }

            headers.Add(entry);
        }

        if (request.ContentType is not null
            && !request.Headers.Any(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
        {
            headers.Add(new JsonObject { ["key"] = "Content-Type", ["value"] = request.ContentType });
        }

        var query = new JsonArray();
        foreach (var parameter in request.QueryParameters)
        {
            var entry = new JsonObject { ["key"] = parameter.Name, ["value"] = parameter.Value };
            if (!parameter.Enabled)
            {
                entry["disabled"] = true;
            }

            query.Add(entry);
        }

        // Postman rebuilds host and path from "raw". Only enabled parameters belong in raw; disabled
        // ones are kept in the "query" list so they survive the round trip switched off.
        var enabled = request.QueryParameters.Where(q => q.Enabled).Select(q => $"{q.Name}={q.Value}").ToList();
        var raw = enabled.Count == 0 ? request.Url : $"{request.Url}{(request.Url.Contains('?') ? '&' : '?')}{string.Join('&', enabled)}";
        var url = new JsonObject { ["raw"] = raw };
        if (query.Count > 0)
        {
            url["query"] = query;
        }

        var body = new JsonObject();
        var exported = new JsonObject
        {
            ["method"] = request.Method,
            ["header"] = headers,
            ["url"] = url,
        };

        if (request.Body is not null)
        {
            body["mode"] = "raw";
            body["raw"] = request.Body;
            if (LanguageFor(request.ContentType) is { } language)
            {
                body["options"] = new JsonObject { ["raw"] = new JsonObject { ["language"] = language } };
            }

            exported["body"] = body;
        }

        if (ExportAuth(request.Auth) is { } auth)
        {
            exported["auth"] = auth;
        }

        return new JsonObject { ["name"] = request.Name, ["request"] = exported };
    }

    private static JsonObject? ExportAuth(AuthSettings auth) => auth.Type switch
    {
        AuthTypes.None => new JsonObject { ["type"] = "noauth" },
        AuthTypes.Bearer => new JsonObject
        {
            ["type"] = "bearer",
            ["bearer"] = new JsonArray(Pair("token", auth.Value ?? "")),
        },
        AuthTypes.ApiKey => new JsonObject
        {
            ["type"] = "apikey",
            ["apikey"] = new JsonArray(
                Pair("key", auth.KeyName ?? ""),
                Pair("value", auth.Value ?? ""),
                Pair("in", auth.KeyLocation ?? ApiKeyLocations.Header)),
        },
        _ => null, // inherit: Postman's default when "auth" is absent.
    };

    private static JsonObject Pair(string key, string value) =>
        new() { ["key"] = key, ["value"] = value, ["type"] = "string" };

    private static string? LanguageFor(string? contentType) => contentType?.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "application/json" => "json",
        "application/xml" or "text/xml" => "xml",
        "text/html" => "html",
        "text/plain" => "text",
        "application/javascript" => "javascript",
        _ => null,
    };

    private static string? ContentTypeFor(string? language) => language switch
    {
        "json" => "application/json",
        "xml" => "application/xml",
        "html" => "text/html",
        "text" => "text/plain",
        "javascript" => "application/javascript",
        _ => null,
    };

    /// <summary>Walks the item tree, collecting warnings and enforcing the size limits.</summary>
    private sealed class Reader
    {
        public List<string> Warnings { get; } = [];
        public int RequestCount { get; private set; }
        public bool TooLarge { get; private set; }

        private int _folderCount;
        private readonly HashSet<string> _warned = [];

        public List<PostmanItem> ReadItems(JsonElement items, int depth, string path)
        {
            var result = new List<PostmanItem>();
            foreach (var item in items.EnumerateArray())
            {
                if (TooLarge || item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = Limit(GetString(item, "name"), CollectionRules.NameMaxLength) ?? "Untitled";
                var itemPath = path.Length == 0 ? name : $"{path} / {name}";

                if (item.TryGetProperty("item", out var children) && children.ValueKind == JsonValueKind.Array)
                {
                    if (depth > CollectionRules.MaxFolderDepth)
                    {
                        Warnings.Add($"\"{itemPath}\" is nested more than {CollectionRules.MaxFolderDepth} folders deep and was skipped.");
                        continue;
                    }

                    if (++_folderCount > MaxImportedFolders)
                    {
                        TooLarge = true;
                        continue;
                    }

                    if (item.TryGetProperty("auth", out var folderAuth) && folderAuth.ValueKind == JsonValueKind.Object)
                    {
                        Warnings.Add($"Folder auth on \"{itemPath}\" wasn't imported.");
                    }

                    result.Add(new PostmanItem(name, null, ReadItems(children, depth + 1, itemPath)));
                }
                else if (item.TryGetProperty("request", out var request))
                {
                    if (++RequestCount > MaxImportedRequests)
                    {
                        TooLarge = true;
                        continue;
                    }

                    if (ReadRequest(name, itemPath, request) is { } parsed)
                    {
                        result.Add(new PostmanItem(name, parsed, []));
                    }
                    else
                    {
                        RequestCount--;
                    }
                }
            }

            return result;
        }

        private PostmanRequest? ReadRequest(string name, string path, JsonElement request)
        {
            // A request may be a bare URL string, which means GET.
            if (request.ValueKind == JsonValueKind.String)
            {
                return Build(name, path, "GET", request.GetString() ?? "", null, [], null, null, new AuthSettings());
            }

            if (request.ValueKind != JsonValueKind.Object)
            {
                Warnings.Add($"\"{path}\" has no readable request and was skipped.");
                return null;
            }

            var method = (GetString(request, "method") ?? "GET").ToUpperInvariant();
            if (!CollectionRules.Methods.Contains(method))
            {
                Warnings.Add($"\"{path}\" uses the {method} method, which isn't supported, and was skipped.");
                return null;
            }

            var (url, query) = ReadUrl(request);
            var headers = ReadPairs(request, "header");
            var (body, contentType) = ReadBody(path, request);
            var auth = ReadAuth(path, request);
            return Build(name, path, method, url, query, headers, body, contentType, auth);
        }

        private PostmanRequest? Build(
            string name, string path, string method, string url, List<KeyValueEntry>? query,
            List<KeyValueEntry> headers, string? body, string? contentType, AuthSettings auth)
        {
            // A raw URL with its query string becomes a base URL plus separate parameters,
            // unless Postman already listed the parameters.
            var questionMark = url.IndexOf('?');
            if (questionMark >= 0)
            {
                query ??= ParseQueryString(url[(questionMark + 1)..]);
                url = url[..questionMark];
            }

            query ??= [];

            // A Content-Type header in the file wins over the body language, and moves into ContentType.
            var contentTypeHeader = headers.FirstOrDefault(h => h.Enabled && h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            if (contentTypeHeader is not null && body is not null)
            {
                contentType = contentTypeHeader.Value;
                headers.Remove(contentTypeHeader);
            }

            if (url.Length > CollectionRules.UrlMaxLength
                || (body?.Length ?? 0) > CollectionRules.BodyMaxLength
                || headers.Count > CollectionRules.MaxEntries
                || query.Count > CollectionRules.MaxEntries
                || headers.Concat(query).Any(e => e.Name.Length > CollectionRules.KeyMaxLength || e.Value.Length > CollectionRules.ValueMaxLength)
                || (contentType?.Length ?? 0) > CollectionRules.ContentTypeMaxLength)
            {
                Warnings.Add($"\"{path}\" is larger than a saved request allows and was skipped.");
                return null;
            }

            return new PostmanRequest(name, method, url.Trim(), headers, query, body, contentType, auth);
        }

        private static (string Url, List<KeyValueEntry>? Query) ReadUrl(JsonElement request)
        {
            if (!request.TryGetProperty("url", out var url))
            {
                return ("", null);
            }

            if (url.ValueKind == JsonValueKind.String)
            {
                return (url.GetString() ?? "", null);
            }

            if (url.ValueKind != JsonValueKind.Object)
            {
                return ("", null);
            }

            var raw = GetString(url, "raw") ?? "";
            var query = url.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.Array ? ReadPairs(url, "query") : null;
            return (raw, query);
        }

        private (string? Body, string? ContentType) ReadBody(string path, JsonElement request)
        {
            if (!request.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            switch (GetString(body, "mode"))
            {
                case "raw":
                    var raw = GetString(body, "raw");
                    var language = body.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Object
                        && options.TryGetProperty("raw", out var rawOptions) && rawOptions.ValueKind == JsonValueKind.Object
                            ? GetString(rawOptions, "language")
                            : null;
                    return (string.IsNullOrEmpty(raw) ? null : raw, ContentTypeFor(language));

                case "urlencoded":
                    var fields = ReadPairs(body, "urlencoded").Where(f => f.Enabled)
                        .Select(f => $"{Uri.EscapeDataString(f.Name)}={Uri.EscapeDataString(f.Value)}");
                    return (string.Join('&', fields), "application/x-www-form-urlencoded");

                case "graphql" when body.TryGetProperty("graphql", out var graphql) && graphql.ValueKind == JsonValueKind.Object:
                    // GraphQL over HTTP is a JSON body with "query" and optional "variables".
                    var payload = new JsonObject { ["query"] = GetString(graphql, "query") ?? "" };
                    var variablesText = GetString(graphql, "variables");
                    if (!string.IsNullOrWhiteSpace(variablesText))
                    {
                        try
                        {
                            payload["variables"] = JsonNode.Parse(variablesText);
                        }
                        catch (JsonException)
                        {
                            Warn($"The GraphQL variables of \"{path}\" aren't valid JSON and were left out.");
                        }
                    }

                    return (payload.ToJsonString(), "application/json");

                case null or "none":
                    return (null, null);

                default:
                    Warn($"\"{path}\" has a {GetString(body, "mode")} body, which isn't supported. The request was imported without a body.");
                    return (null, null);
            }
        }

        private AuthSettings ReadAuth(string path, JsonElement request)
        {
            if (!request.TryGetProperty("auth", out var auth) || auth.ValueKind != JsonValueKind.Object)
            {
                return new AuthSettings { Type = AuthTypes.Inherit };
            }

            var type = GetString(auth, "type");
            var values = auth.TryGetProperty(type ?? "", out var list) ? ReadAuthValues(list) : [];
            switch (type)
            {
                case "noauth":
                    return new AuthSettings { Type = AuthTypes.None };
                case "bearer":
                    return new AuthSettings { Type = AuthTypes.Bearer, Value = values.GetValueOrDefault("token", "") };
                case "apikey":
                    return new AuthSettings
                    {
                        Type = AuthTypes.ApiKey,
                        KeyName = values.GetValueOrDefault("key", "X-API-Key"),
                        Value = values.GetValueOrDefault("value", ""),
                        KeyLocation = values.GetValueOrDefault("in") == "query" ? ApiKeyLocations.Query : ApiKeyLocations.Header,
                    };
                default:
                    Warn($"\"{path}\" uses {type} auth, which isn't supported. It will use the environment's auth instead.");
                    return new AuthSettings { Type = AuthTypes.Inherit };
            }
        }

        /// <summary>Postman stores auth settings as a list of key/value pairs (v2.1) or as an object (v2.0).</summary>
        private static Dictionary<string, string> ReadAuthValues(JsonElement element)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var pair in element.EnumerateArray())
                {
                    if (pair.ValueKind == JsonValueKind.Object && GetString(pair, "key") is { } key)
                    {
                        values[key] = GetString(pair, "value") ?? "";
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        values[property.Name] = property.Value.GetString()!;
                    }
                }
            }

            return values;
        }

        private void Warn(string message)
        {
            if (_warned.Add(message))
            {
                Warnings.Add(message);
            }
        }
    }

    private static List<KeyValueEntry> ReadPairs(JsonElement parent, string property)
    {
        var entries = new List<KeyValueEntry>();
        if (!parent.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        foreach (var pair in array.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(GetString(pair, "key")))
            {
                continue;
            }

            var disabled = pair.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True;
            entries.Add(new KeyValueEntry { Name = GetString(pair, "key")!.Trim(), Value = GetString(pair, "value") ?? "", Enabled = !disabled });
        }

        return entries;
    }

    private static List<KeyValueEntry> ParseQueryString(string query) =>
        query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var equals = part.IndexOf('=');
                var name = equals < 0 ? part : part[..equals];
                var value = equals < 0 ? "" : part[(equals + 1)..];
                return new KeyValueEntry { Name = Unescape(name), Value = Unescape(value) };
            })
            .ToList();

    /// <summary>Decodes %XX and '+' but leaves {{variables}} and malformed escapes as they are.</summary>
    private static string Unescape(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A description is either a string or an object with a "content" string.</summary>
    private static string? GetDescription(JsonElement info) =>
        info.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.Object
            ? GetString(description, "content")
            : GetString(info, "description");

    private static string? Limit(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
