using System.Text.Json;
using Microsoft.OpenApi;

namespace ApiArchitect.Api.Features.ClientCode;

/// <summary>
/// Maps OpenAPI schemas to C# types, and collects the model classes they need. Only schemas the chosen operations
/// use become classes. Anything without a plain C# shape (oneOf, mixed types, free-form objects) becomes
/// <c>JsonElement</c>, so the code always compiles and the data is still there to read.
/// </summary>
public sealed class ClientModels
{
    public sealed record Property(string Name, string JsonName, string Type, string? Description);

    public sealed record Model(string Name, string? Description, List<Property> Properties);

    private static readonly HashSet<string> ValueTypes =
        ["int", "long", "float", "double", "decimal", "bool", "Guid", "DateTimeOffset", "DateOnly", "TimeOnly", "JsonElement"];

    private readonly Dictionary<string, string> _namesByReference = new(StringComparer.Ordinal);
    private readonly HashSet<string> _referencesInProgress = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reservedNames;
    private readonly HashSet<string> _takenNames;

    /// <param name="reservedNames">Names the rest of the code uses. A schema called Task becomes TaskModel.</param>
    public ClientModels(IEnumerable<string> reservedNames)
    {
        _reservedNames = new HashSet<string>(reservedNames, StringComparer.Ordinal);
        _takenNames = new HashSet<string>(_reservedNames, StringComparer.Ordinal);
    }

    public List<Model> Models { get; } = [];

    public static bool IsValueType(string type) => ValueTypes.Contains(type);

    /// <summary>True for types that can go in a URL or header as text.</summary>
    public static bool IsSimple(string type) =>
        type == "string" || (IsValueType(type) && type != "JsonElement")
        || (type.StartsWith("List<", StringComparison.Ordinal) && IsSimple(type[5..^1]));

    /// <summary>The C# type for <paramref name="schema"/>, without a trailing <c>?</c>.</summary>
    /// <param name="nameHint">The class name to use if the schema is an inline object.</param>
    public string Map(IOpenApiSchema? schema, string nameHint)
    {
        if (schema is null)
        {
            return "JsonElement";
        }

        if (schema is OpenApiSchemaReference { Reference.Id: { } id })
        {
            if (_namesByReference.TryGetValue(id, out var known))
            {
                return known;
            }

            if (IsObject(schema))
            {
                var name = ClassName(CSharpNames.Pascal(id));
                _namesByReference[id] = name;
                AddModel(name, schema);
                return name;
            }

            // A named string, array or enum. Guard against a schema that refers back to itself.
            if (!_referencesInProgress.Add(id))
            {
                return "JsonElement";
            }

            var type = MapInline(schema, CSharpNames.Pascal(id));
            _referencesInProgress.Remove(id);
            return type;
        }

        return MapInline(schema, nameHint);
    }

    private string MapInline(IOpenApiSchema schema, string nameHint)
    {
        var alternatives = (schema.OneOf ?? []).Concat(schema.AnyOf ?? []).Where(s => !IsNullOnly(s)).ToList();
        if (alternatives.Count == 1 && schema.Properties is not { Count: > 0 })
        {
            return Map(alternatives[0], nameHint);
        }

        if (alternatives.Count > 1)
        {
            return "JsonElement";
        }

        if (IsObject(schema))
        {
            var name = ClassName(nameHint);
            AddModel(name, schema);
            return name;
        }

        if (schema.AllOf is [var single])
        {
            return Map(single, nameHint);
        }

        var type = schema.Type & ~JsonSchemaType.Null;

        // ASP.NET Core describes numbers as ["integer", "string"] because it also accepts "42". The client's
        // JSON options read both, so these are plain numbers.
        if (type is (JsonSchemaType.Integer | JsonSchemaType.String) or (JsonSchemaType.Number | JsonSchemaType.String))
        {
            type &= ~JsonSchemaType.String;
        }

        return type switch
        {
            JsonSchemaType.String => schema.Format switch
            {
                "date-time" => "DateTimeOffset",
                "date" => "DateOnly",
                "uuid" => "Guid",
                _ => "string",
            },
            JsonSchemaType.Integer => schema.Format == "int64" ? "long" : "int",
            JsonSchemaType.Number => schema.Format switch
            {
                "float" => "float",
                "decimal" => "decimal",
                _ => "double",
            },
            JsonSchemaType.Boolean => "bool",
            JsonSchemaType.Array => $"List<{Map(schema.Items, nameHint + "Item")}>",
            JsonSchemaType.Object when schema.AdditionalProperties is { } values =>
                $"Dictionary<string, {Map(values, nameHint + "Value")}>",
            _ => "JsonElement",
        };
    }

    /// <summary>
    /// The C# type for a path, query or header parameter. Objects can't be sent as text, so they become a string
    /// the caller formats, and no model class is made for them.
    /// </summary>
    public string MapParameter(IOpenApiSchema? schema)
    {
        var items = schema?.Type?.HasFlag(JsonSchemaType.Array) == true ? schema.Items : schema;
        if (items is null || IsObject(items) || items.Type?.HasFlag(JsonSchemaType.Object) == true)
        {
            return "string";
        }

        var type = Map(schema, "Parameter");
        return IsSimple(type) ? type : "string";
    }

    private string ClassName(string name) =>
        CSharpNames.Unique(_reservedNames.Contains(name) ? name + "Model" : name, _takenNames);

    /// <summary>An object with named properties, possibly built from allOf parts.</summary>
    private static bool IsObject(IOpenApiSchema schema) =>
        schema.Properties is { Count: > 0 }
        || (schema.AllOf is { Count: > 1 } parts && parts.All(IsObject))
        || (schema.AllOf is [var only] && IsObject(only));

    private static bool IsNullOnly(IOpenApiSchema schema) => schema.Type == JsonSchemaType.Null;

    private void AddModel(string name, IOpenApiSchema schema)
    {
        // Added before its properties are mapped, so a property that points back at this schema finds it.
        var model = new Model(name, schema.Description ?? schema.Title, []);
        Models.Add(model);

        var takenPropertyNames = new HashSet<string>(StringComparer.Ordinal) { name };
        foreach (var (jsonName, propertySchema) in AllProperties(schema))
        {
            var propertyName = CSharpNames.Pascal(jsonName);
            if (propertyName == name)
            {
                propertyName += "Value";
            }

            propertyName = CSharpNames.Unique(propertyName, takenPropertyNames);
            var type = Map(propertySchema, name + propertyName);
            model.Properties.Add(new Property(propertyName, jsonName, type, propertySchema.Description));
        }
    }

    /// <summary>The schema's own properties plus those of its allOf parts. A later one with the same name wins.</summary>
    private static IEnumerable<KeyValuePair<string, IOpenApiSchema>> AllProperties(IOpenApiSchema schema)
    {
        var properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        foreach (var part in schema.AllOf ?? [])
        {
            foreach (var (key, value) in AllProperties(part))
            {
                properties[key] = value;
            }
        }

        foreach (var (key, value) in schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
        {
            properties[key] = value;
        }

        return properties;
    }

    /// <summary>Models.cs: one class per model, with nullable get/set properties.</summary>
    public string WriteFile(string rootNamespace)
    {
        var body = new CodeWriter();
        var needsAttributes = false;
        foreach (var model in Models)
        {
            body.Line();
            body.Summary(model.Description);
            body.Line($"public class {model.Name}");
            body.Line("{");
            for (var i = 0; i < model.Properties.Count; i++)
            {
                var property = model.Properties[i];
                if (i > 0)
                {
                    body.Line();
                }

                body.Summary(property.Description, indent: "    ");

                // The client reads and writes camelCase names. Other names need to be spelled out.
                if (JsonNamingPolicy.CamelCase.ConvertName(property.Name) != property.JsonName)
                {
                    body.Line($"    [JsonPropertyName({CSharpNames.Literal(property.JsonName)})]");
                    needsAttributes = true;
                }

                body.Line($"    public {property.Type}? {property.Name} {{ get; set; }}");
            }

            body.Line("}");
        }

        var file = new CodeWriter();
        var text = body.ToString();
        if (text.Contains("JsonElement", StringComparison.Ordinal))
        {
            file.Line("using System.Text.Json;");
        }

        if (needsAttributes)
        {
            file.Line("using System.Text.Json.Serialization;");
        }

        if (file.Length > 0)
        {
            file.Line();
        }

        file.Line($"namespace {rootNamespace};");
        file.Raw(text);
        return file.ToString();
    }
}
