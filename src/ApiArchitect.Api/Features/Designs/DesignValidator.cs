using System.Text.RegularExpressions;

namespace ApiArchitect.Api.Features.Designs;

/// <summary>
/// Checks a design before it's saved, so that everything the generators produce from it is valid C# and valid
/// OpenAPI. Messages name the model or endpoint they're about, because the editor shows them in one list.
/// </summary>
public static partial class DesignValidator
{
    /// <summary>Validates <paramref name="design"/>. Returns errors keyed by field path; empty when it's valid.</summary>
    public static Dictionary<string, string[]> Validate(DesignDocument? design)
    {
        var errors = new Errors();
        if (design is null)
        {
            errors.Add("Document", "The design is missing.");
            return errors.ToDictionary();
        }

        Text(errors, "Document.Title", "The API title", design.Title, DesignRules.TitleMaxLength, required: true);
        Text(errors, "Document.Version", "The API version", design.Version, DesignRules.VersionMaxLength, required: true);
        Text(errors, "Document.Description", "The API description", design.Description, DesignRules.DescriptionMaxLength, required: false);
        if (design.Namespace is null || design.Namespace.Length > 100 || !NamespacePattern().IsMatch(design.Namespace))
        {
            errors.Add("Document.Namespace", "The namespace must be PascalCase identifiers separated by dots, such as Bookstore.Api.");
        }

        if (design.Models is null || design.Endpoints is null)
        {
            errors.Add("Document", "The design needs a models list and an endpoints list (they can be empty).");
            return errors.ToDictionary();
        }

        if (design.Models.Count > DesignRules.MaxModels)
        {
            errors.Add("Document.Models", $"A design can have at most {DesignRules.MaxModels} models.");
        }

        if (design.Endpoints.Count > DesignRules.MaxEndpoints)
        {
            errors.Add("Document.Endpoints", $"A design can have at most {DesignRules.MaxEndpoints} endpoints.");
        }

        var modelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in design.Models)
        {
            if (model?.Name is not null && !modelNames.Add(model.Name))
            {
                errors.Add("Document.Models", $"There's more than one model called {model.Name}. Model names must be unique (ignoring case).");
            }
        }

        // Exact-case lookup for references: C# is case-sensitive.
        var models = design.Models.Where(m => m?.Name is not null).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < design.Models.Count; i++)
        {
            ValidateModel(errors, $"Document.Models[{i}]", design.Models[i], models);
        }

        var endpointNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < design.Endpoints.Count; i++)
        {
            var endpoint = design.Endpoints[i];
            var path = $"Document.Endpoints[{i}]";
            if (endpoint is null)
            {
                errors.Add(path, $"Endpoint {i + 1} is empty.");
                continue;
            }

            ValidateEndpoint(errors, path, endpoint, models);
            if (endpoint.Name is not null && !endpointNames.Add(endpoint.Name))
            {
                errors.Add($"{path}.Name", $"There's more than one endpoint called {endpoint.Name}. Endpoint names become operation ids and must be unique.");
            }

            if (endpoint.Method is not null && endpoint.Route is not null
                && !operations.Add($"{endpoint.Method.ToUpperInvariant()} {RouteParameter().Replace(endpoint.Route, "{}")}"))
            {
                errors.Add($"{path}.Route", $"{endpoint.Method} {endpoint.Route} is defined more than once.");
            }
        }

        return errors.ToDictionary();
    }

    private static void ValidateModel(Errors errors, string path, ModelDefinition? model, HashSet<string> models)
    {
        if (model is null)
        {
            errors.Add(path, "A model is empty.");
            return;
        }

        var label = string.IsNullOrWhiteSpace(model.Name) ? "A model" : $"Model {model.Name}";
        TypeName(errors, $"{path}.Name", label, model.Name);
        Text(errors, $"{path}.Description", $"{label}'s description", model.Description, DesignRules.DescriptionMaxLength, required: false);

        if (model.Properties is null || model.Properties.Count > DesignRules.MaxPropertiesPerModel)
        {
            errors.Add($"{path}.Properties", $"{label} can have up to {DesignRules.MaxPropertiesPerModel} properties.");
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < model.Properties.Count; i++)
        {
            var property = model.Properties[i];
            var propertyPath = $"{path}.Properties[{i}]";
            if (property is null)
            {
                errors.Add(propertyPath, $"{label} has an empty property.");
                continue;
            }

            var propertyLabel = $"{label}, property {(string.IsNullOrWhiteSpace(property.Name) ? i + 1 : property.Name)}";
            if (property.Name is null || property.Name.Length > DesignRules.IdentifierMaxLength || !TypeNamePattern().IsMatch(property.Name))
            {
                errors.Add($"{propertyPath}.Name", $"{propertyLabel}: the name must be a PascalCase C# identifier, such as FirstName.");
            }
            else if (property.Name == model.Name)
            {
                errors.Add($"{propertyPath}.Name", $"{propertyLabel}: a property can't have the same name as its model (C# doesn't allow it).");
            }
            else if (!names.Add(property.Name))
            {
                // Case-insensitive: Name and name would both serialize to "name" in camelCase JSON.
                errors.Add($"{propertyPath}.Name", $"{propertyLabel}: {model.Name} already has a property with this name.");
            }

            ValidateProperty(errors, propertyPath, propertyLabel, property, models);
        }
    }

    private static void ValidateProperty(Errors errors, string path, string label, PropertyDefinition property, HashSet<string> models)
    {
        Text(errors, $"{path}.Description", $"{label}: the description", property.Description, DesignRules.DescriptionMaxLength, required: false);

        DesignTypes.Simple.TryGetValue(property.Type ?? "", out var simple);
        if (simple is null && !models.Contains(property.Type ?? ""))
        {
            errors.Add($"{path}.Type", $"{label}: the type must be a simple type or one of the design's models.");
            return;
        }

        var isString = simple?.IsString == true && !property.IsList;
        var isNumber = simple?.IsNumeric == true && !property.IsList;
        const string StringRule = "length, pattern and format rules only apply to a single string";

        if (property.MinLength is not null || property.MaxLength is not null)
        {
            if (!isString)
            {
                errors.Add($"{path}.MinLength", $"{label}: {StringRule}.");
            }
            else if (property.MinLength < 0 || property.MaxLength < 1 || property.MinLength > property.MaxLength)
            {
                errors.Add($"{path}.MinLength", $"{label}: the minimum length must be 0 or more, the maximum 1 or more, and the minimum can't be above the maximum.");
            }
        }

        if (property.Pattern is not null)
        {
            if (!isString)
            {
                errors.Add($"{path}.Pattern", $"{label}: {StringRule}.");
            }
            else if (property.Pattern.Length is 0 or > DesignRules.PatternMaxLength || !IsValidRegex(property.Pattern))
            {
                errors.Add($"{path}.Pattern", $"{label}: the pattern isn't a valid regular expression (up to {DesignRules.PatternMaxLength} characters).");
            }
        }

        if (property.Format is not null && (!isString || !DesignTypes.Formats.Contains(property.Format)))
        {
            errors.Add($"{path}.Format", $"{label}: the format must be email or url, and only applies to a single string.");
        }

        if (property.Minimum is not null || property.Maximum is not null)
        {
            if (!isNumber)
            {
                errors.Add($"{path}.Minimum", $"{label}: minimum and maximum only apply to a single number.");
            }
            else if (property.Minimum > property.Maximum)
            {
                errors.Add($"{path}.Minimum", $"{label}: the minimum can't be above the maximum.");
            }
        }
    }

    private static void ValidateEndpoint(Errors errors, string path, EndpointDefinition endpoint, HashSet<string> models)
    {
        var label = string.IsNullOrWhiteSpace(endpoint.Name) ? $"Endpoint {endpoint.Method} {endpoint.Route}" : $"Endpoint {endpoint.Name}";

        TypeName(errors, $"{path}.Name", label, endpoint.Name);
        if (endpoint.Resource is null || endpoint.Resource.Length > DesignRules.IdentifierMaxLength || !TypeNamePattern().IsMatch(endpoint.Resource))
        {
            errors.Add($"{path}.Resource", $"{label}: the resource must be a PascalCase name, such as Books.");
        }

        Text(errors, $"{path}.Summary", $"{label}: the summary", endpoint.Summary, DesignRules.DescriptionMaxLength, required: false);

        var method = endpoint.Method ?? "";
        if (!DesignTypes.Methods.Contains(method))
        {
            errors.Add($"{path}.Method", $"{label}: the method must be one of {string.Join(", ", DesignTypes.Methods)}.");
        }

        var routeParameters = ValidateRoute(errors, $"{path}.Route", label, endpoint.Route);

        if (endpoint.Parameters is null || endpoint.Parameters.Count > DesignRules.MaxParametersPerEndpoint)
        {
            errors.Add($"{path}.Parameters", $"{label} can have up to {DesignRules.MaxParametersPerEndpoint} parameters.");
        }
        else
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in endpoint.Parameters)
            {
                if (parameter is null)
                {
                    errors.Add($"{path}.Parameters", $"{label} has an empty parameter.");
                    continue;
                }

                var parameterLabel = $"{label}, parameter {parameter.Name}";
                if (parameter.Name is null || parameter.Name.Length > DesignRules.IdentifierMaxLength
                    || !ParameterNamePattern().IsMatch(parameter.Name) || ReservedParameterNames.Contains(parameter.Name))
                {
                    errors.Add($"{path}.Parameters", $"{parameterLabel}: the name must be a camelCase identifier, such as pageSize, and not a C# keyword.");
                }
                else if (!names.Add(parameter.Name))
                {
                    errors.Add($"{path}.Parameters", $"{parameterLabel} is defined more than once.");
                }

                if (parameter.In is not ("path" or "query"))
                {
                    errors.Add($"{path}.Parameters", $"{parameterLabel}: a parameter is either in the path or the query.");
                }

                if (!DesignTypes.Simple.ContainsKey(parameter.Type ?? ""))
                {
                    errors.Add($"{path}.Parameters", $"{parameterLabel}: parameters must have a simple type, such as string, int or guid.");
                }

                Text(errors, $"{path}.Parameters", $"{parameterLabel}: the description", parameter.Description, DesignRules.DescriptionMaxLength, required: false);
            }

            var pathParameters = endpoint.Parameters.Where(p => p?.In == "path").Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var missing in routeParameters.Except(pathParameters))
            {
                errors.Add($"{path}.Parameters", $"{label}: the route uses {{{missing}}}, so it needs a path parameter called {missing}.");
            }

            foreach (var extra in pathParameters.Except(routeParameters))
            {
                errors.Add($"{path}.Parameters", $"{label}: path parameter {extra} isn't in the route. Add {{{extra}}} to the route or make it a query parameter.");
            }
        }

        if (endpoint.RequestModel is not null)
        {
            if (method is "GET" or "DELETE")
            {
                errors.Add($"{path}.RequestModel", $"{label}: {method} requests don't have a body. Use query parameters instead.");
            }
            else if (!models.Contains(endpoint.RequestModel))
            {
                errors.Add($"{path}.RequestModel", $"{label}: the request model {endpoint.RequestModel} doesn't exist.");
            }
        }

        if (!DesignTypes.SuccessStatuses.Contains(endpoint.SuccessStatus))
        {
            errors.Add($"{path}.SuccessStatus", $"{label}: the success status must be 200, 201, 202 or 204.");
        }

        if (endpoint.ResponseModel is not null)
        {
            if (!models.Contains(endpoint.ResponseModel))
            {
                errors.Add($"{path}.ResponseModel", $"{label}: the response model {endpoint.ResponseModel} doesn't exist.");
            }
            else if (endpoint.SuccessStatus == 204)
            {
                errors.Add($"{path}.ResponseModel", $"{label}: a 204 No Content response can't have a body.");
            }
        }
        else if (endpoint.ResponseIsList)
        {
            errors.Add($"{path}.ResponseIsList", $"{label}: choose the response model the list contains.");
        }

        if (endpoint.ErrorStatuses is null
            || endpoint.ErrorStatuses.Any(s => !DesignTypes.ErrorStatuses.Contains(s))
            || endpoint.ErrorStatuses.Distinct().Count() != endpoint.ErrorStatuses.Count)
        {
            errors.Add($"{path}.ErrorStatuses", $"{label}: error statuses can be 400, 401, 403, 404 and 409, each once.");
        }
    }

    /// <summary>Checks the route template and returns the names of its <c>{parameters}</c>.</summary>
    private static HashSet<string> ValidateRoute(Errors errors, string path, string label, string? route)
    {
        var parameters = new HashSet<string>(StringComparer.Ordinal);
        if (route is null || route.Length > DesignRules.RouteMaxLength || !route.StartsWith('/'))
        {
            errors.Add(path, $"{label}: the route must start with / and be at most {DesignRules.RouteMaxLength} characters.");
            return parameters;
        }

        if (route == "/")
        {
            return parameters;
        }

        foreach (var segment in route[1..].Split('/'))
        {
            var parameter = RouteParameter().Match(segment);
            if (parameter.Success && parameter.Length == segment.Length)
            {
                if (!parameters.Add(parameter.Groups[1].Value))
                {
                    errors.Add(path, $"{label}: {{{parameter.Groups[1].Value}}} appears in the route more than once.");
                }
            }
            else if (!RouteLiteralPattern().IsMatch(segment))
            {
                errors.Add(path, $"{label}: \"{segment}\" isn't a valid route segment. Use letters, digits, - _ . ~ or a {{parameter}}, with no empty segments.");
            }
        }

        return parameters;
    }

    private static void TypeName(Errors errors, string path, string label, string? name)
    {
        if (name is null || name.Length > DesignRules.IdentifierMaxLength || !TypeNamePattern().IsMatch(name))
        {
            errors.Add(path, $"{label}: the name must be a PascalCase C# identifier, such as BookResponse.");
        }
        else if (ReservedTypeNames.Contains(name))
        {
            errors.Add(path, $"{label}: {name} clashes with a name the generated code already uses. Pick another.");
        }
    }

    private static void Text(Errors errors, string path, string label, string? value, int maxLength, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add(path, $"{label} is required.");
        }
        else if (value?.Length > maxLength)
        {
            errors.Add(path, $"{label} can be at most {maxLength} characters.");
        }
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Names that would collide with types the generated project uses or the framework types it imports.</summary>
    private static readonly HashSet<string> ReservedTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program", "Results", "TypedResults", "Task", "Guid", "DateTimeOffset", "DateOnly", "String", "Object",
        "List", "Ok", "Created", "Accepted", "NoContent", "NotFound", "Conflict", "ValidationProblem",
        "UnauthorizedHttpResult", "ForbidHttpResult", "CancellationToken", "WebApplication", "Endpoints",
    };

    /// <summary>C# keywords, plus the parameter names every generated handler already has.</summary>
    private static readonly HashSet<string> ReservedParameterNames = new(StringComparer.Ordinal)
    {
        "request", "cancellationToken",
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)*$")]
    private static partial Regex NamespacePattern();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex TypeNamePattern();

    [GeneratedRegex("^[a-z][A-Za-z0-9]*$")]
    private static partial Regex ParameterNamePattern();

    [GeneratedRegex(@"\{([a-z][A-Za-z0-9]*)\}")]
    internal static partial Regex RouteParameter();

    [GeneratedRegex(@"^[A-Za-z0-9._~-]+$")]
    private static partial Regex RouteLiteralPattern();

    /// <summary>Collects messages per field, keeping the order they were found in.</summary>
    private sealed class Errors
    {
        private readonly Dictionary<string, List<string>> _errors = [];

        public void Add(string path, string message)
        {
            if (!_errors.TryGetValue(path, out var messages))
            {
                _errors[path] = messages = [];
            }

            if (!messages.Contains(message))
            {
                messages.Add(message);
            }
        }

        public Dictionary<string, string[]> ToDictionary() => _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }
}
