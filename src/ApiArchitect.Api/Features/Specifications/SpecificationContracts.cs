using System.ComponentModel.DataAnnotations;

namespace ApiArchitect.Api.Features.Specifications;

/// <summary>Body for importing an OpenAPI document, or replacing an imported one.</summary>
/// <param name="Name">Display name in the project, 1 to 100 characters.</param>
/// <param name="Content">The OpenAPI 3.x document as JSON text, up to 5,000,000 characters.</param>
public sealed record SpecificationRequest(
    [Required, StringLength(SpecificationRules.NameMaxLength)] string Name,
    [Required, StringLength(SpecificationRules.ContentMaxLength)] string Content);

/// <summary>An imported specification, without its content. Fetch the document itself from its document URL.</summary>
/// <param name="Title">The document's <c>info.title</c>.</param>
/// <param name="Version">The document's <c>info.version</c>.</param>
/// <param name="OpenApiVersion">The document's <c>openapi</c> version, such as 3.1.0.</param>
public sealed record SpecificationResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Title,
    string Version,
    string OpenApiVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
