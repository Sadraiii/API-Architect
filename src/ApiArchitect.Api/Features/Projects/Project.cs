namespace ApiArchitect.Api.Features.Projects;

public sealed class Project
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string OwnerId { get; init; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Target framework moniker of the API being documented, such as <c>net11.0</c>.</summary>
    public string? TargetFramework { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class ProjectRules
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 2000;
}
