namespace ApiArchitect.Api.Features.Collections;

/// <summary>Moves saved requests between their stored form and the API contracts.</summary>
internal static class SavedRequestMapping
{
    public static SavedRequestResponse ToResponse(SavedRequest r) => new(
        r.Id,
        r.CollectionId,
        r.FolderId,
        r.Name,
        r.Method,
        r.Url,
        ToDtos(r.Headers),
        ToDtos(r.QueryParameters),
        r.Body,
        r.ContentType,
        new AuthDto(r.Auth.Type, r.Auth.Value, r.Auth.KeyName, r.Auth.KeyLocation),
        r.CreatedAt,
        r.UpdatedAt);

    /// <summary>Copies a validated input onto a request. The folder is set by the caller after checking it.</summary>
    public static void Apply(SavedRequestInput input, SavedRequest request)
    {
        request.Name = input.Name.Trim();
        request.Method = input.Method;
        request.Url = input.Url?.Trim() ?? "";
        request.Headers = ToEntries(input.Headers);
        request.QueryParameters = ToEntries(input.QueryParameters);
        request.Body = string.IsNullOrEmpty(input.Body) ? null : input.Body;
        request.ContentType = string.IsNullOrWhiteSpace(input.ContentType) ? null : input.ContentType.Trim();
        request.Auth = ToAuth(input.Auth);
    }

    /// <summary>Rules DataAnnotations can't express. Returns field errors, or an empty dictionary.</summary>
    public static Dictionary<string, string[]> Validate(SavedRequestInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.Auth is { Type: AuthTypes.ApiKey } auth
            && (string.IsNullOrWhiteSpace(auth.KeyName) || auth.KeyLocation is null))
        {
            errors["Auth.KeyName"] = ["An API key needs a name and a location (header or query)."];
        }

        if (input.Headers?.Any(h => string.IsNullOrWhiteSpace(h.Name)) == true)
        {
            errors["Headers"] = ["Every header needs a name."];
        }

        if (input.QueryParameters?.Any(q => string.IsNullOrWhiteSpace(q.Name)) == true)
        {
            errors["QueryParameters"] = ["Every query parameter needs a name."];
        }

        return errors;
    }

    /// <summary>A deep copy for "Duplicate", so editing the copy never touches the original's header or auth objects.</summary>
    public static SavedRequest Copy(SavedRequest source, string name, DateTimeOffset now) => new()
    {
        CollectionId = source.CollectionId,
        FolderId = source.FolderId,
        Name = name,
        Method = source.Method,
        Url = source.Url,
        Headers = source.Headers.Select(Clone).ToList(),
        QueryParameters = source.QueryParameters.Select(Clone).ToList(),
        Body = source.Body,
        ContentType = source.ContentType,
        Auth = new AuthSettings
        {
            Type = source.Auth.Type,
            Value = source.Auth.Value,
            KeyName = source.Auth.KeyName,
            KeyLocation = source.Auth.KeyLocation,
        },
        CreatedAt = now,
        UpdatedAt = now,
    };

    public static List<KeyValueDto> ToDtos(List<KeyValueEntry> entries) =>
        entries.Select(e => new KeyValueDto(e.Name, e.Value, e.Enabled)).ToList();

    public static List<KeyValueEntry> ToEntries(List<KeyValueDto>? dtos) =>
        dtos?.Select(d => new KeyValueEntry { Name = d.Name.Trim(), Value = d.Value ?? "", Enabled = d.Enabled }).ToList() ?? [];

    private static AuthSettings ToAuth(AuthDto? dto) => dto switch
    {
        null or { Type: AuthTypes.Inherit } => new AuthSettings { Type = AuthTypes.Inherit },
        { Type: AuthTypes.None } => new AuthSettings { Type = AuthTypes.None },
        { Type: AuthTypes.Bearer } => new AuthSettings { Type = AuthTypes.Bearer, Value = dto.Value ?? "" },
        _ => new AuthSettings
        {
            Type = AuthTypes.ApiKey,
            Value = dto.Value ?? "",
            KeyName = dto.KeyName?.Trim(),
            KeyLocation = dto.KeyLocation,
        },
    };

    private static KeyValueEntry Clone(KeyValueEntry e) => new() { Name = e.Name, Value = e.Value, Enabled = e.Enabled };
}
