using System.ComponentModel.DataAnnotations;

namespace ApiArchitect.Api.Features.Projects;

/// <summary>Body for creating or replacing a project.</summary>
/// <param name="Name">Display name, 1 to 100 characters.</param>
/// <param name="Description">Optional description, up to 2000 characters.</param>
/// <param name="TargetFramework">Optional target framework: net8.0, net9.0, net10.0 or net11.0.</param>
public sealed record ProjectRequest(
    [Required, StringLength(ProjectRules.NameMaxLength)] string Name,
    [StringLength(ProjectRules.DescriptionMaxLength)] string? Description,
    [AllowedValues(null, "net8.0", "net9.0", "net10.0", "net11.0")] string? TargetFramework);

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    string? TargetFramework,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
