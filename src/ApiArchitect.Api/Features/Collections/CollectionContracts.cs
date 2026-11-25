using System.ComponentModel.DataAnnotations;

namespace ApiArchitect.Api.Features.Collections;

/// <summary>Body for creating or renaming a collection.</summary>
public sealed record CollectionInput(
    [Required, StringLength(CollectionRules.NameMaxLength)] string Name,
    [StringLength(CollectionRules.DescriptionMaxLength)] string? Description);

public sealed record CollectionSummary(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    int RequestCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A collection with its whole tree as two flat lists. Build the tree from <c>ParentFolderId</c> and
/// <c>FolderId</c>; null means the top level of the collection.
/// </summary>
public sealed record CollectionDetail(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<FolderResponse> Folders,
    List<RequestSummary> Requests);

/// <summary>Body for creating, renaming or moving a folder.</summary>
/// <param name="Name">Folder name.</param>
/// <param name="ParentFolderId">The folder to put it in, or null for the top level.</param>
public sealed record FolderInput(
    [Required, StringLength(CollectionRules.NameMaxLength)] string Name,
    Guid? ParentFolderId);

public sealed record FolderResponse(Guid Id, string Name, Guid? ParentFolderId);

public sealed record RequestSummary(Guid Id, Guid? FolderId, string Name, string Method, DateTimeOffset UpdatedAt);

/// <summary>A header or query parameter. Disabled entries are saved but not sent.</summary>
public sealed record KeyValueDto(
    [Required, StringLength(CollectionRules.KeyMaxLength)] string Name,
    [StringLength(CollectionRules.ValueMaxLength)] string? Value,
    bool Enabled = true);

/// <param name="Type">inherit (use the environment's auth), none, bearer or apiKey.</param>
/// <param name="Value">The bearer token or API key value. Prefer a {{variable}} pointing at a secret.</param>
/// <param name="KeyName">API key only: the header or query parameter name.</param>
/// <param name="KeyLocation">API key only: header or query.</param>
public sealed record AuthDto(
    [Required, AllowedValues(AuthTypes.Inherit, AuthTypes.None, AuthTypes.Bearer, AuthTypes.ApiKey)] string Type,
    [StringLength(CollectionRules.ValueMaxLength)] string? Value,
    [StringLength(CollectionRules.KeyMaxLength)] string? KeyName,
    [AllowedValues(null, ApiKeyLocations.Header, ApiKeyLocations.Query)] string? KeyLocation);

/// <summary>Body for creating or replacing a saved request.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Method">GET, POST, PUT, PATCH, DELETE, HEAD or OPTIONS.</param>
/// <param name="Url">Absolute URL, or a path to append to the environment's base URL. May contain {{variables}}.</param>
/// <param name="FolderId">The folder to put it in, or null for the top level of the collection.</param>
/// <param name="Headers">Headers, up to 100.</param>
/// <param name="QueryParameters">Query parameters, up to 100. Added to the URL when the request runs.</param>
/// <param name="Body">Request body text, up to 1 MB.</param>
/// <param name="ContentType">Content-Type of the body, such as application/json.</param>
/// <param name="Auth">Authentication. Omit to inherit from the environment.</param>
public sealed record SavedRequestInput(
    [Required, StringLength(CollectionRules.NameMaxLength)] string Name,
    [Required, AllowedValues("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")] string Method,
    [Required, StringLength(CollectionRules.UrlMaxLength)] string Url,
    Guid? FolderId,
    [MaxLength(CollectionRules.MaxEntries)] List<KeyValueDto>? Headers,
    [MaxLength(CollectionRules.MaxEntries)] List<KeyValueDto>? QueryParameters,
    [StringLength(CollectionRules.BodyMaxLength)] string? Body,
    [StringLength(CollectionRules.ContentTypeMaxLength)] string? ContentType,
    AuthDto? Auth);

public sealed record SavedRequestResponse(
    Guid Id,
    Guid CollectionId,
    Guid? FolderId,
    string Name,
    string Method,
    string Url,
    List<KeyValueDto> Headers,
    List<KeyValueDto> QueryParameters,
    string? Body,
    string? ContentType,
    AuthDto Auth,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The result of importing a Postman collection.</summary>
/// <param name="Collection">The new collection.</param>
/// <param name="Warnings">Parts of the file that could not be imported, in plain English.</param>
public sealed record CollectionImportResponse(CollectionSummary Collection, List<string> Warnings);
