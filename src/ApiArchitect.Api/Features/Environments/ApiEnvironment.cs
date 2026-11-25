using ApiArchitect.Api.Features.Collections;
using ApiArchitect.Api.Features.Projects;

namespace ApiArchitect.Api.Features.Environments;

/// <summary>
/// A target to run requests against, such as Local, Staging or Production: a base URL, variables,
/// headers sent with every request, and default authentication.
/// Named ApiEnvironment so it doesn't clash with <see cref="System.Environment"/>.
/// </summary>
public sealed class ApiEnvironment
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProjectId { get; init; }

    public Project Project { get; init; } = null!;

    public required string Name { get; set; }

    /// <summary>Prefixed to saved request URLs that are a path, and available as <c>{{baseUrl}}</c>.</summary>
    public string? BaseUrl { get; set; }

    public List<EnvironmentVariable> Variables { get; set; } = [];

    /// <summary>Headers added to every request run in this environment. A request's own header of the same name wins.</summary>
    public List<KeyValueEntry> Headers { get; set; } = [];

    public EnvironmentAuth Auth { get; set; } = new();

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EnvironmentVariable
{
    public required string Name { get; set; }

    /// <summary>The value as typed, or for a secret, its Data Protection ciphertext.</summary>
    public string Value { get; set; } = "";

    /// <summary>Secrets are encrypted at rest and never returned by the API.</summary>
    public bool IsSecret { get; set; }
}

/// <summary>Default auth for requests set to "inherit". The token or key value is always stored encrypted.</summary>
public sealed class EnvironmentAuth
{
    /// <summary><c>none</c>, <c>bearer</c> or <c>apiKey</c>.</summary>
    public string Type { get; set; } = AuthTypes.None;

    /// <summary>Data Protection ciphertext of the bearer token or API key value.</summary>
    public string? ProtectedValue { get; set; }

    public string? KeyName { get; set; }

    public string? KeyLocation { get; set; }
}

public static class EnvironmentRules
{
    public const int NameMaxLength = 100;
    public const int BaseUrlMaxLength = 2000;
    public const int VariableNameMaxLength = 100;
    public const int MaxVariables = 200;

    /// <summary>Letters, digits, '_', '-' and '.', starting with a letter or '_'. Matches what {{...}} accepts.</summary>
    public const string VariableNamePattern = "^[A-Za-z_][A-Za-z0-9_.-]*$";

    /// <summary>The variable that exposes <see cref="ApiEnvironment.BaseUrl"/> unless one is defined with this name.</summary>
    public const string BaseUrlVariable = "baseUrl";
}
