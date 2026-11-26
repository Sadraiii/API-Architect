using System.ComponentModel.DataAnnotations;

namespace ApiArchitect.Api.Features.Designs;

/// <summary>Body for creating a design.</summary>
/// <param name="Name">Display name in the project, 1 to 100 characters.</param>
/// <param name="StartFrom"><c>blank</c> (the default) or <c>example</c>, a small Bookstore API to learn from.</param>
public sealed record CreateDesignRequest(
    [Required, StringLength(DesignRules.NameMaxLength)] string Name,
    [AllowedValues(null, "blank", "example")] string? StartFrom);

/// <summary>Body for saving a design.</summary>
/// <param name="Name">Display name in the project, 1 to 100 characters.</param>
/// <param name="Document">The whole design. It's validated as a unit and replaces what was stored.</param>
public sealed record UpdateDesignRequest(
    [Required, StringLength(DesignRules.NameMaxLength)] string Name,
    [Required] DesignDocument Document);

/// <summary>A design in a project's list, without its document.</summary>
/// <param name="SpecificationId">The specification it was last saved to, if any.</param>
public sealed record DesignSummaryResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    Guid? SpecificationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A design with its document.</summary>
public sealed record DesignResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    DesignDocument Document,
    Guid? SpecificationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
