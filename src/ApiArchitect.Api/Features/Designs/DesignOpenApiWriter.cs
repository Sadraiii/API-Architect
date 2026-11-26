using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiArchitect.Api.Features.Designs;

/// <summary>
/// Turns a design into an OpenAPI 3.1 document. It describes the same API the generated project serves:
/// camelCase JSON property names, ProblemDetails errors and JWT bearer auth.
/// </summary>
/// <remarks>
/// Built as JSON nodes rather than through Microsoft.OpenApi's object model: the output is a handful of
/// fixed shapes, and writing them directly keeps key order stable and the code easy to follow.
/// The tests run every generated document back through <c>OpenApiImport</c> to prove it parses.
/// </remarks>
public static class DesignOpenApiWriter
{
    public const string OpenApiVersion = "3.1.1";

    private const string BearerScheme = "Bearer";
    private const string ProblemSchema = "ProblemDetails";
    private const string ValidationProblemSchema = "HttpValidationProblemDetails";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(DesignDocument design) => Build(design).ToJsonString(Indented);

    public static JsonObject Build(DesignDocument design)
    {
        var info = new JsonObject { ["title"] = design.Title, ["version"] = design.Version };
        if (!string.IsNullOrWhiteSpace(design.Description))
        {
            info["description"] = design.Description;
        }

        var document = new JsonObject
        {
            ["openapi"] = OpenApiVersion,
            ["info"] = info,
            ["paths"] = BuildPaths(design),
        };

        var tags = design.Endpoints.Select(e => e.Resource).Distinct(StringComparer.Ordinal).ToList();
        if (tags.Count > 0)
        {
            document["tags"] = new JsonArray(tags.Select(t => (JsonNode)new JsonObject { ["name"] = t }).ToArray());
        }

        var schemas = new JsonObject();
        foreach (var model in design.Models)
        {
            schemas[model.Name] = ModelSchema(model);
        }

        if (design.Endpoints.Any(e => e.ErrorStatuses.Count > 0))
        {
            schemas[ProblemSchema] = ProblemDetailsSchema(withErrors: false);
            schemas[ValidationProblemSchema] = ProblemDetailsSchema(withErrors: true);
        }

        var components = new JsonObject();
        if (schemas.Count > 0)
        {
            components["schemas"] = schemas;
        }

        if (design.Endpoints.Any(e => e.RequiresAuth))
        {
            components["securitySchemes"] = new JsonObject
            {
                [BearerScheme] = new JsonObject
                {
                    ["type"] = "http",
                    ["scheme"] = "bearer",
                    ["bearerFormat"] = "JWT",
                    ["description"] = "A JWT access token in the Authorization header.",
                },
            };
        }

        if (components.Count > 0)
        {
            document["components"] = components;
        }

        return document;
    }

    private static JsonObject BuildPaths(DesignDocument design)
    {
        var paths = new JsonObject();
        foreach (var endpoint in design.Endpoints)
        {
            if (paths[endpoint.Route] is not JsonObject pathItem)
            {
                paths[endpoint.Route] = pathItem = new JsonObject();
            }

            pathItem[endpoint.Method.ToLowerInvariant()] = Operation(endpoint);
        }

        return paths;
    }

    private static JsonObject Operation(EndpointDefinition endpoint)
    {
        var operation = new JsonObject
        {
            ["tags"] = new JsonArray(endpoint.Resource),
            ["operationId"] = endpoint.Name,
        };

        if (!string.IsNullOrWhiteSpace(endpoint.Summary))
        {
            operation["summary"] = endpoint.Summary;
        }

        if (endpoint.Parameters.Count > 0)
        {
            operation["parameters"] = new JsonArray(endpoint.Parameters.Select(p => (JsonNode)Parameter(p)).ToArray());
        }

        if (endpoint.RequestModel is not null)
        {
            operation["requestBody"] = new JsonObject
            {
                ["required"] = true,
                ["content"] = JsonContent("application/json", Ref(endpoint.RequestModel)),
            };
        }

        var responses = new JsonObject();
        var success = new JsonObject { ["description"] = StatusDescription(endpoint.SuccessStatus) };
        if (endpoint.ResponseModel is not null)
        {
            var schema = endpoint.ResponseIsList
                ? new JsonObject { ["type"] = "array", ["items"] = Ref(endpoint.ResponseModel) }
                : Ref(endpoint.ResponseModel);
            success["content"] = JsonContent("application/json", schema);
        }

        responses[Code(endpoint.SuccessStatus)] = success;

        foreach (var status in endpoint.ErrorStatuses.Order())
        {
            var schema = status == 400 ? ValidationProblemSchema : ProblemSchema;
            responses[Code(status)] = new JsonObject
            {
                ["description"] = StatusDescription(status),
                ["content"] = JsonContent("application/problem+json", Ref(schema)),
            };
        }

        operation["responses"] = responses;

        if (endpoint.RequiresAuth)
        {
            operation["security"] = new JsonArray(new JsonObject { [BearerScheme] = new JsonArray() });
        }

        return operation;
    }

    private static JsonObject Parameter(ParameterDefinition parameter)
    {
        var node = new JsonObject
        {
            ["name"] = parameter.Name,
            ["in"] = parameter.In,
            // OpenAPI requires path parameters to be required.
            ["required"] = parameter.In == "path" || parameter.Required,
        };

        if (!string.IsNullOrWhiteSpace(parameter.Description))
        {
            node["description"] = parameter.Description;
        }

        node["schema"] = SimpleSchema(DesignTypes.Simple[parameter.Type]);
        return node;
    }

    private static JsonObject ModelSchema(ModelDefinition model)
    {
        var schema = new JsonObject { ["type"] = "object" };
        if (!string.IsNullOrWhiteSpace(model.Description))
        {
            schema["description"] = model.Description;
        }

        var required = model.Properties.Where(p => p.Required).Select(p => (JsonNode)JsonName(p.Name)).ToArray();
        if (required.Length > 0)
        {
            schema["required"] = new JsonArray(required);
        }

        var properties = new JsonObject();
        foreach (var property in model.Properties)
        {
            properties[JsonName(property.Name)] = PropertySchema(property);
        }

        schema["properties"] = properties;
        return schema;
    }

    private static JsonNode PropertySchema(PropertyDefinition property)
    {
        JsonObject item;
        if (DesignTypes.Simple.TryGetValue(property.Type, out var simple))
        {
            item = SimpleSchema(simple);
            if (property.MinLength is not null) item["minLength"] = property.MinLength;
            if (property.MaxLength is not null) item["maxLength"] = property.MaxLength;
            if (property.Minimum is not null) item["minimum"] = property.Minimum;
            if (property.Maximum is not null) item["maximum"] = property.Maximum;
            // RegularExpressionAttribute matches the whole value; OpenAPI patterns are unanchored.
            if (property.Pattern is not null) item["pattern"] = $"^(?:{property.Pattern})$";
            if (property.Format is not null) item["format"] = property.Format == "url" ? "uri" : property.Format;
        }
        else
        {
            item = Ref(property.Type);
        }

        JsonObject schema = property.IsList ? new JsonObject { ["type"] = "array", ["items"] = item } : item;

        // Optional properties are nullable in the generated C# (string?, int?), so they can be null in JSON too.
        if (!property.Required)
        {
            schema = Nullable(schema);
        }

        if (!string.IsNullOrWhiteSpace(property.Description))
        {
            schema["description"] = property.Description;
        }

        return schema;
    }

    /// <summary>OpenAPI 3.1 has no <c>nullable</c>: a type becomes <c>[type, "null"]</c>, and a $ref goes in a oneOf.</summary>
    private static JsonObject Nullable(JsonObject schema)
    {
        if (schema["type"] is JsonValue type)
        {
            schema["type"] = new JsonArray(type.GetValue<string>(), "null");
            return schema;
        }

        return new JsonObject { ["oneOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };
    }

    private static JsonObject SimpleSchema(DesignTypes.TypeInfo type)
    {
        var schema = new JsonObject { ["type"] = type.OpenApiType };
        if (type.OpenApiFormat is not null)
        {
            schema["format"] = type.OpenApiFormat;
        }

        return schema;
    }

    private static JsonObject ProblemDetailsSchema(bool withErrors)
    {
        var properties = new JsonObject
        {
            ["type"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["title"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["status"] = new JsonObject { ["type"] = new JsonArray("integer", "null"), ["format"] = "int32" },
            ["detail"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["instance"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
        };

        if (withErrors)
        {
            properties["errors"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = withErrors
                ? "RFC 9457 problem details with the validation errors for each field."
                : "RFC 9457 problem details.",
            ["properties"] = properties,
        };
    }

    private static JsonObject JsonContent(string mediaType, JsonNode schema) =>
        new() { [mediaType] = new JsonObject { ["schema"] = schema } };

    private static JsonObject Ref(string schema) => new() { ["$ref"] = $"#/components/schemas/{schema}" };

    private static string Code(int status) => status.ToString(CultureInfo.InvariantCulture);

    /// <summary>The JSON name ASP.NET Core's default (camelCase) serializer gives a C# property.</summary>
    private static string JsonName(string propertyName) => JsonNamingPolicy.CamelCase.ConvertName(propertyName);

    private static string StatusDescription(int status) => status switch
    {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a status the designer supports."),
    };
}
