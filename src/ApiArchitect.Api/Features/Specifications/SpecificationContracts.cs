using System.ComponentModel.DataAnnotations;

namespace ApiArchitect.Api.Features.Specifications;

/// <summary>Body for importing an OpenAPI document, or replacing an imported one.</summary>
/// <param name="Name">Display name in the project, 1 to 100 characters.</param>
/// <param name="Content">The OpenAPI 3.x document as JSON text, up to 5,000,000 characters.</param>
public sealed record SpecificationRequest(
    [Required, StringLength(SpecificationRules.NameMaxLength)] string Name,
    [Required, StringLength(SpecificationRules.ContentMaxLength)] string Content);

/// <summary>Body for importing a document from a URL into an existing project.</summary>
/// <param name="Url">An http or https URL that returns OpenAPI 3.x JSON, such as https://localhost:5001/openapi/v1.json.</param>
/// <param name="Name">Display name. Defaults to the document's <c>info.title</c>.</param>
public sealed record SpecificationUrlRequest(
    [Required, StringLength(SpecificationRules.SourceUrlMaxLength)] string Url,
    [StringLength(SpecificationRules.NameMaxLength)] string? Name);

/// <summary>Body for creating a new project from a document. Send exactly one of Content or Url.</summary>
/// <param name="Content">The OpenAPI 3.x document as JSON text.</param>
/// <param name="Url">An http or https URL that returns the document.</param>
public sealed record ProjectFromSpecificationRequest(
    [StringLength(SpecificationRules.ContentMaxLength)] string? Content,
    [StringLength(SpecificationRules.SourceUrlMaxLength)] string? Url);

/// <summary>The result of a re-import: the updated specification and what changed in it.</summary>
public sealed record ReimportResponse(SpecificationResponse Specification, SpecificationChanges Changes);

/// <summary>An imported specification, without its content. Fetch the document itself from its document URL.</summary>
/// <param name="Title">The document's <c>info.title</c>.</param>
/// <param name="Version">The document's <c>info.version</c>.</param>
/// <param name="OpenApiVersion">The document's <c>openapi</c> version, such as 3.1.0.</param>
/// <param name="SourceUrl">Where the document was imported from, if it came from a URL.</param>
public sealed record SpecificationResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Title,
    string Version,
    string OpenApiVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? SourceUrl = null);
