using ApiArchitect.Api.Features.Projects;
using ApiArchitect.Api.Features.Specifications;

namespace ApiArchitect.Api.Features.Designs;

/// <summary>
/// An API being designed in the app: its models and endpoints. The OpenAPI document and the generated
/// ASP.NET Core project are both produced from it on request, so neither can drift from the design.
/// </summary>
public sealed class ApiDesign
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProjectId { get; init; }

    /// <summary>The owning project. Ownership checks go through <c>Project.OwnerId</c>.</summary>
    public Project Project { get; init; } = null!;

    public required string Name { get; set; }

    /// <summary>
    /// The <see cref="DesignDocument"/> as JSON. It's only ever read and written whole, so a text column
    /// keeps this simple; nothing queries inside it.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>The specification this design was last saved to, if any. Saving again updates that one.</summary>
    public Guid? SpecificationId { get; set; }

    public ApiSpecification? Specification { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class DesignRules
{
    public const int NameMaxLength = 100;
    public const int TitleMaxLength = 200;
    public const int VersionMaxLength = 50;
    public const int DescriptionMaxLength = 1000;
    public const int IdentifierMaxLength = 64;
    public const int RouteMaxLength = 300;
    public const int PatternMaxLength = 300;

    public const int MaxModels = 100;
    public const int MaxPropertiesPerModel = 60;
    public const int MaxEndpoints = 200;
    public const int MaxParametersPerEndpoint = 20;

    /// <summary>Generous for the limits above; stops a client from posting megabytes of JSON.</summary>
    public const long RequestBodyMaxBytes = 2 * 1024 * 1024;
}
