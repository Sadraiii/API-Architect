using ApiArchitect.Api.Features.Projects;

namespace ApiArchitect.Api.Features.Specifications;

/// <summary>An OpenAPI document imported into a project.</summary>
public sealed class ApiSpecification
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProjectId { get; init; }

    /// <summary>The owning project. Ownership checks go through <c>Project.OwnerId</c>.</summary>
    public Project Project { get; init; } = null!;

    public required string Name { get; set; }

    /// <summary>
    /// The document exactly as it was imported. Stored as <c>text</c> rather than <c>jsonb</c>, because jsonb
    /// reorders keys and drops formatting, and export should give back what was imported.
    /// </summary>
    public required string Content { get; set; }

    /// <summary><c>info.title</c>, copied out of the document so lists don't have to load <see cref="Content"/>.</summary>
    public required string Title { get; set; }

    /// <summary><c>info.version</c>: the version of the described API, not of OpenAPI.</summary>
    public required string Version { get; set; }

    /// <summary>The document's <c>openapi</c> field, such as <c>3.1.0</c>.</summary>
    public required string OpenApiVersion { get; set; }

    /// <summary>Where the document was downloaded from, when it was imported from a URL. Used by re-import.</summary>
    public string? SourceUrl { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SpecificationRules
{
    public const int NameMaxLength = 100;
    public const int TitleMaxLength = 200;
    public const int VersionMaxLength = 50;
    public const int OpenApiVersionMaxLength = 20;
    public const int SourceUrlMaxLength = 2048;

    /// <summary>5 million characters. Large real-world documents (Stripe, GitHub) are 2–4 MB of JSON.</summary>
    public const int ContentMaxLength = 5_000_000;

    /// <summary>Request body cap for import and replace: the content limit plus room for JSON escaping.</summary>
    public const long RequestBodyMaxBytes = 12 * 1024 * 1024;
}
