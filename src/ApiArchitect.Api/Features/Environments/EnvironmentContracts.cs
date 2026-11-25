using System.ComponentModel.DataAnnotations;
using ApiArchitect.Api.Features.Collections;

namespace ApiArchitect.Api.Features.Environments;

/// <summary>Body for creating or replacing an environment.</summary>
/// <param name="Name">Display name, such as Staging.</param>
/// <param name="BaseUrl">Optional absolute http(s) URL that request paths are appended to.</param>
/// <param name="Variables">Variables, up to 200. Names must be unique.</param>
/// <param name="Headers">Headers sent with every request in this environment, up to 100.</param>
/// <param name="Auth">Default auth for requests set to inherit. Omit for none.</param>
public sealed record EnvironmentInput(
    [Required, StringLength(EnvironmentRules.NameMaxLength)] string Name,
    [StringLength(EnvironmentRules.BaseUrlMaxLength)] string? BaseUrl,
    [MaxLength(EnvironmentRules.MaxVariables)] List<VariableInput>? Variables,
    [MaxLength(CollectionRules.MaxEntries)] List<KeyValueDto>? Headers,
    EnvironmentAuthInput? Auth);

/// <param name="Name">Used as {{Name}} in requests.</param>
/// <param name="Value">
/// The value. For a secret, send null to keep the stored value, which is how the UI saves an environment
/// without ever having seen its secrets.
/// </param>
/// <param name="IsSecret">Encrypt the value at rest and never return it.</param>
public sealed record VariableInput(
    [Required, StringLength(EnvironmentRules.VariableNameMaxLength), RegularExpression(EnvironmentRules.VariableNamePattern)] string Name,
    [StringLength(CollectionRules.ValueMaxLength)] string? Value,
    bool IsSecret = false);

/// <param name="Type">none, bearer or apiKey.</param>
/// <param name="Value">The token or key value. Send null to keep the stored one. Stored encrypted, never returned.</param>
/// <param name="KeyName">API key only: header or query parameter name.</param>
/// <param name="KeyLocation">API key only: header or query.</param>
public sealed record EnvironmentAuthInput(
    [Required, AllowedValues(AuthTypes.None, AuthTypes.Bearer, AuthTypes.ApiKey)] string Type,
    [StringLength(CollectionRules.ValueMaxLength)] string? Value,
    [StringLength(CollectionRules.KeyMaxLength)] string? KeyName,
    [AllowedValues(null, ApiKeyLocations.Header, ApiKeyLocations.Query)] string? KeyLocation);

/// <summary>An environment as the API returns it. Secret values are never included.</summary>
public sealed record EnvironmentResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? BaseUrl,
    List<VariableResponse> Variables,
    List<KeyValueDto> Headers,
    EnvironmentAuthResponse Auth,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="Value">The value, or null when the variable is a secret.</param>
public sealed record VariableResponse(string Name, string? Value, bool IsSecret);

/// <param name="HasValue">Whether a token or key value is stored. The value itself is never returned.</param>
public sealed record EnvironmentAuthResponse(string Type, string? KeyName, string? KeyLocation, bool HasValue);
