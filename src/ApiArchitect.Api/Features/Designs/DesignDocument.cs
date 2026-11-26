namespace ApiArchitect.Api.Features.Designs;

// The shape of an API design. It is the request body when saving a design, what gets stored, and the input
// to both generators, so it stays deliberately small: only what maps cleanly onto both OpenAPI and C#.
// DesignValidator checks it before anything is saved; the generators assume a valid document.

/// <summary>An API design: its metadata, models (DTOs) and endpoints.</summary>
public sealed record DesignDocument
{
    /// <summary>API title, used for <c>info.title</c>.</summary>
    public string Title { get; init; } = "";
    /// <summary>API version, used for <c>info.version</c>, such as 1.0.0.</summary>
    public string Version { get; init; } = "";
    public string? Description { get; init; }
    /// <summary>Root namespace and project name of the generated code, such as <c>Bookstore.Api</c>.</summary>
    public string Namespace { get; init; } = "";
    public List<ModelDefinition> Models { get; init; } = [];
    public List<EndpointDefinition> Endpoints { get; init; } = [];
}

/// <summary>A DTO: becomes a C# record and an OpenAPI schema under <c>components.schemas</c>.</summary>
public sealed record ModelDefinition
{
    /// <summary>PascalCase C# type name, such as <c>CreateBookRequest</c>.</summary>
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public List<PropertyDefinition> Properties { get; init; } = [];
}

/// <summary>A model property. Validation rules become DataAnnotations in C# and keywords in the schema.</summary>
public sealed record PropertyDefinition
{
    /// <summary>PascalCase C# property name. JSON uses the camelCase form, as ASP.NET Core does by default.</summary>
    public string Name { get; init; } = "";
    /// <summary>A key from <see cref="DesignTypes"/> (string, int, guid…) or the name of another model.</summary>
    public string Type { get; init; } = "";
    /// <summary>True for a JSON array of <see cref="Type"/>.</summary>
    public bool IsList { get; init; }
    /// <summary>Must be present (and, for strings, not empty).</summary>
    public bool Required { get; init; }
    public string? Description { get; init; }
    /// <summary>Strings only: minimum length.</summary>
    public int? MinLength { get; init; }
    /// <summary>Strings only: maximum length.</summary>
    public int? MaxLength { get; init; }
    /// <summary>Numbers only: inclusive minimum.</summary>
    public decimal? Minimum { get; init; }
    /// <summary>Numbers only: inclusive maximum.</summary>
    public decimal? Maximum { get; init; }
    /// <summary>Strings only: a regular expression the whole value must match.</summary>
    public string? Pattern { get; init; }
    /// <summary>Strings only: <c>email</c> or <c>url</c>.</summary>
    public string? Format { get; init; }
}

/// <summary>One operation. Becomes an OpenAPI operation and a minimal API handler.</summary>
public sealed record EndpointDefinition
{
    /// <summary>Groups endpoints: the OpenAPI tag and the generated <c>XxxEndpoints</c> class, such as <c>Books</c>.</summary>
    public string Resource { get; init; } = "";
    /// <summary>GET, POST, PUT, PATCH or DELETE.</summary>
    public string Method { get; init; } = "";
    /// <summary>Route template, such as <c>/api/books/{id}</c>.</summary>
    public string Route { get; init; } = "";
    /// <summary>PascalCase handler name and OpenAPI operationId, such as <c>GetBook</c>.</summary>
    public string Name { get; init; } = "";
    public string? Summary { get; init; }
    /// <summary>Route and query parameters. Every <c>{name}</c> in the route needs a path parameter.</summary>
    public List<ParameterDefinition> Parameters { get; init; } = [];
    /// <summary>Model read from the JSON body, if any. Not allowed for GET and DELETE.</summary>
    public string? RequestModel { get; init; }
    /// <summary>Model returned with the success status, if any.</summary>
    public string? ResponseModel { get; init; }
    /// <summary>The success response is a JSON array of <see cref="ResponseModel"/>.</summary>
    public bool ResponseIsList { get; init; }
    /// <summary>200, 201, 202 or 204.</summary>
    public int SuccessStatus { get; init; } = 200;
    /// <summary>Any of 400, 401, 403, 404 and 409.</summary>
    public List<int> ErrorStatuses { get; init; } = [];
    /// <summary>Needs an authenticated caller (JWT bearer in the generated project).</summary>
    public bool RequiresAuth { get; init; }
}

/// <summary>A route or query parameter. Only simple types; complex input goes in the request model.</summary>
public sealed record ParameterDefinition
{
    /// <summary>camelCase name, matching the route template for path parameters.</summary>
    public string Name { get; init; } = "";
    /// <summary><c>path</c> or <c>query</c>.</summary>
    public string In { get; init; } = "query";
    /// <summary>A key from <see cref="DesignTypes"/>.</summary>
    public string Type { get; init; } = "string";
    /// <summary>Query parameters only; path parameters are always required.</summary>
    public bool Required { get; init; }
    public string? Description { get; init; }
}

/// <summary>The simple types a property or parameter can have, and how each maps to C# and OpenAPI.</summary>
public static class DesignTypes
{
    public sealed record TypeInfo(string CSharp, bool IsValueType, string OpenApiType, string? OpenApiFormat, bool IsNumeric, bool IsString);

    public static readonly IReadOnlyDictionary<string, TypeInfo> Simple = new Dictionary<string, TypeInfo>
    {
        ["string"] = new("string", false, "string", null, false, true),
        ["int"] = new("int", true, "integer", "int32", true, false),
        ["long"] = new("long", true, "integer", "int64", true, false),
        ["decimal"] = new("decimal", true, "number", null, true, false),
        ["double"] = new("double", true, "number", "double", true, false),
        ["bool"] = new("bool", true, "boolean", null, false, false),
        ["guid"] = new("Guid", true, "string", "uuid", false, false),
        ["datetime"] = new("DateTimeOffset", true, "string", "date-time", false, false),
        ["date"] = new("DateOnly", true, "string", "date", false, false),
    };

    public static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    public static readonly int[] SuccessStatuses = [200, 201, 202, 204];
    public static readonly int[] ErrorStatuses = [400, 401, 403, 404, 409];
    public static readonly string[] Formats = ["email", "url"];
}
