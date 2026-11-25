using ApiArchitect.Api.Features.Projects;

namespace ApiArchitect.Api.Features.Collections;

/// <summary>A named set of saved requests inside a project, optionally organised into folders.</summary>
public sealed class RequestCollection
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProjectId { get; init; }

    public Project Project { get; init; } = null!;

    public required string Name { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A folder in a collection. Folders can nest; deleting one deletes everything inside it.</summary>
public sealed class RequestFolder
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid CollectionId { get; init; }

    public RequestCollection Collection { get; init; } = null!;

    /// <summary>The containing folder, or null for a folder at the top of the collection.</summary>
    public Guid? ParentFolderId { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// An HTTP request saved in a collection. Every text field may contain <c>{{variable}}</c> placeholders,
/// which are filled from the selected environment when the request runs.
/// </summary>
/// <remarks>
/// Lists are ordered by <see cref="Id"/>: version 7 GUIDs sort by creation time, so the order people
/// created (or imported) requests in is kept without a separate position column.
/// </remarks>
public sealed class SavedRequest
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid CollectionId { get; init; }

    public RequestCollection Collection { get; init; } = null!;

    /// <summary>The containing folder, or null for a request at the top of the collection.</summary>
    public Guid? FolderId { get; set; }

    public required string Name { get; set; }

    public required string Method { get; set; }

    /// <summary>Absolute URL, or a path that is appended to the environment's base URL.</summary>
    public required string Url { get; set; }

    public List<KeyValueEntry> Headers { get; set; } = [];

    public List<KeyValueEntry> QueryParameters { get; set; } = [];

    public string? Body { get; set; }

    public string? ContentType { get; set; }

    public AuthSettings Auth { get; set; } = new();

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A header or query parameter. Disabled entries are kept but not sent.</summary>
public sealed class KeyValueEntry
{
    public required string Name { get; set; }

    public string Value { get; set; } = "";

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// How a saved request authenticates. Values are stored as typed, so the UI steers people towards
/// <c>{{variables}}</c> that point at an environment's encrypted secrets instead of pasting tokens here.
/// </summary>
public sealed class AuthSettings
{
    /// <summary>One of <see cref="AuthTypes"/>.</summary>
    public string Type { get; set; } = AuthTypes.Inherit;

    /// <summary>The bearer token, or the API key's value.</summary>
    public string? Value { get; set; }

    /// <summary>API key only: the header or query parameter name.</summary>
    public string? KeyName { get; set; }

    /// <summary>API key only: <c>header</c> or <c>query</c>.</summary>
    public string? KeyLocation { get; set; }
}

public static class AuthTypes
{
    /// <summary>Use the environment's auth configuration.</summary>
    public const string Inherit = "inherit";
    public const string None = "none";
    public const string Bearer = "bearer";
    public const string ApiKey = "apiKey";
}

public static class ApiKeyLocations
{
    public const string Header = "header";
    public const string Query = "query";
}

public static class CollectionRules
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int UrlMaxLength = 8000;
    public const int KeyMaxLength = 500;
    public const int ValueMaxLength = 8000;
    public const int MaxEntries = 100;
    public const int ContentTypeMaxLength = 200;

    /// <summary>1 MB of text: generous for API payloads, small enough to keep rows cheap.</summary>
    public const int BodyMaxLength = 1_000_000;

    /// <summary>Folder nesting limit, so folder trees stay navigable and cycle checks stay cheap.</summary>
    public const int MaxFolderDepth = 10;

    public static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];
}
