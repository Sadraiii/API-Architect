using Microsoft.OpenApi;

namespace ApiArchitect.Api.Features.ClientCode;

/// <summary>
/// Writes plain C# to paste into a new Visual Studio project: a client class with one method per chosen
/// operation, the model classes it needs, and a Program.cs (Console App) or Program.cs and Worker.cs (Worker
/// Service) that use it. Output is fixed templates, so the same input always gives the same code.
/// </summary>
public static class ClientCodeGenerator
{
    private const string ClientName = "ApiClient";
    private const string ConfigSection = "Api";
    private const string FallbackBaseUrl = "https://localhost:5001/";
    private const int DefaultIntervalSeconds = 300;

    /// <summary>The NuGet package the Worker Service needs for AddHttpClient. Its version matches the template's Microsoft.Extensions.Hosting.</summary>
    private const string HttpPackage = "Microsoft.Extensions.Http";

    /// <summary>Names the generated code uses itself, so a model can't take them.</summary>
    private static readonly string[] ReservedNames =
    [
        ClientName, "ApiException", "Program", "Worker", "Task", "List", "Dictionary", "JsonElement", "Uri",
        "Guid", "DateTimeOffset", "DateOnly", "TimeOnly", "HttpClient", "HttpContent", "CancellationToken",
        "Exception", "Console", "Environment", "String", "Object", "Math", "Type",
    ];

    private enum ResultKind { None, Json, Text, Bytes }

    private sealed record Parameter(string Name, string OriginalName, ParameterLocation Location, string Type, bool Required);

    private sealed record Body(string Name, string Type, bool IsJson, bool Required);

    private sealed record Operation(
        string Key, string MethodName, string HttpMethod, string Path, string? Summary,
        List<Parameter> Parameters, Body? Body, ResultKind Result, string? ResultType);

    private sealed record Auth(string Kind, string? HeaderName, string Description);

    /// <summary>The code and steps, or null when none of the requested operations are in the document.</summary>
    public static ClientCodeResponse? Generate(OpenApiDocument document, ClientCodeRequest request)
    {
        var root = request.ProjectName;
        var notes = new List<string>();
        var models = new ClientModels(ReservedNames.Append(root.Split('.')[0]));
        var operations = ReadOperations(document, request.Operations, models, notes);
        if (operations.Count == 0)
        {
            return null;
        }

        var auth = FindAuth(document, operations, notes);
        var baseUrl = BaseUrl(document, notes);
        var isWorker = request.Template == ClientTemplates.Worker;

        var files = new List<ClientCodeFile>
        {
            new($"{ClientName}.cs", "New file. Add > Class, name it ApiClient.cs, and replace everything in it.",
                ClientFile(document, root, operations)),
        };

        if (models.Models.Count > 0)
        {
            files.Add(new("Models.cs", "New file. Add > Class, name it Models.cs, and replace everything in it.",
                models.WriteFile(root)));
        }

        if (isWorker)
        {
            files.Add(new("Program.cs", "Replace everything in the Program.cs the template made.", WorkerProgramFile(root, auth)));
            files.Add(new("Worker.cs", "Replace everything in the Worker.cs the template made.", WorkerFile(root, operations)));
            files.Add(new("appsettings.json", "Replace everything in appsettings.json.", AppSettingsFile(baseUrl)));
            if (auth is not null)
            {
                files.Add(new("secrets.json", "Right-click the project > Manage User Secrets, and paste this in. Put your real key in place of the placeholder.",
                    SecretsFile()));
            }
        }
        else
        {
            files.Add(new("Program.cs", "Replace everything in the Program.cs the template made.",
                ConsoleProgramFile(root, operations, auth, baseUrl)));
        }

        var steps = isWorker ? WorkerSteps(root, models.Models.Count > 0, auth) : ConsoleSteps(root, models.Models.Count > 0, auth);
        return new ClientCodeResponse(request.Template, root, steps, files, notes);
    }

    // ---- Reading the document ----

    private static List<Operation> ReadOperations(
        OpenApiDocument document, IEnumerable<string> keys, ClientModels models, List<string> notes)
    {
        var wanted = keys.Select(NormalizeKey).ToHashSet(StringComparer.Ordinal);
        var methodNames = new HashSet<string>(StringComparer.Ordinal);
        var operations = new List<Operation>();

        foreach (var (path, pathItem) in document.Paths ?? [])
        {
            foreach (var (method, operation) in pathItem.Operations ?? [])
            {
                var key = $"{method.Method.ToUpperInvariant()} {path}";
                if (!wanted.Remove(key))
                {
                    continue;
                }

                if (path.Contains('"') || path.Contains('\\'))
                {
                    notes.Add($"Skipped {key}: its path has characters that can't go in a C# string.");
                    continue;
                }

                operations.Add(ReadOperation(key, method.Method, path, pathItem, operation, models, methodNames, notes));
            }
        }

        notes.AddRange(wanted.Select(key => $"Skipped {key}: it isn't in the specification."));
        return operations;
    }

    /// <summary><c>get /pets</c> and <c>GET /pets</c> are the same operation.</summary>
    private static string NormalizeKey(string key)
    {
        var space = key.IndexOf(' ');
        return space < 0 ? key : key[..space].ToUpperInvariant() + key[space..];
    }

    private static Operation ReadOperation(
        string key, string httpMethod, string path, IOpenApiPathItem pathItem, OpenApiOperation operation,
        ClientModels models, HashSet<string> methodNames, List<string> notes)
    {
        var baseName = string.IsNullOrWhiteSpace(operation.OperationId)
            ? NameFromPath(httpMethod, path)
            : CSharpNames.Pascal(operation.OperationId);
        var methodName = CSharpNames.Unique(baseName, methodNames) + "Async";

        // Path-level parameters apply to every operation; the operation's own win when both define one.
        var declared = (pathItem.Parameters ?? []).Concat(operation.Parameters ?? [])
            .Where(p => p.Name is not null && p.In is not null)
            .GroupBy(p => (p.Name, p.In))
            .Select(g => g.Last())
            .ToList();

        var parameterNames = new HashSet<string>(StringComparer.Ordinal) { "cancellationToken", "request", "response", "url", "query" };
        var parameters = new List<Parameter>();
        foreach (var parameter in declared)
        {
            var location = parameter.In!.Value;
            if (location == ParameterLocation.Cookie)
            {
                notes.Add($"{key}: left out cookie parameter '{parameter.Name}'.");
                continue;
            }

            if (location == ParameterLocation.Header && IsHandledHeader(parameter.Name!))
            {
                continue;
            }

            var type = models.MapParameter(parameter.Schema);
            var required = parameter.Required || location == ParameterLocation.Path;
            parameters.Add(new Parameter(CSharpNames.Unique(CSharpNames.Camel(parameter.Name), parameterNames),
                parameter.Name!, location, type, required));
        }

        // A {name} in the path that no parameter describes still has to be filled in.
        foreach (var segment in PathParameterNames(path))
        {
            if (!parameters.Any(p => p.Location == ParameterLocation.Path && p.OriginalName == segment))
            {
                parameters.Add(new Parameter(CSharpNames.Unique(CSharpNames.Camel(segment), parameterNames),
                    segment, ParameterLocation.Path, "string", true));
            }
        }

        var body = ReadBody(key, operation.RequestBody, baseName, models, parameterNames, notes);
        var (result, resultType) = ReadResult(operation.Responses, baseName, models);
        var summary = operation.Summary ?? operation.Description;
        return new Operation(key, methodName, httpMethod.ToUpperInvariant(), path, summary, parameters, body, result, resultType);
    }

    /// <summary>The client sets these itself, from the HttpClient or the request content.</summary>
    private static bool IsHandledHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Accept", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> PathParameterNames(string path) =>
        path.Split('/')
            .Where(s => s.Length > 2 && s[0] == '{' && s[^1] == '}')
            .Select(s => s[1..^1]);

    /// <summary><c>GET /pets/{id}/toys</c> becomes <c>GetPetsByIdToys</c>.</summary>
    private static string NameFromPath(string httpMethod, string path) =>
        CSharpNames.Pascal(httpMethod.ToLowerInvariant()) + string.Concat(path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.StartsWith('{') ? "By" + CSharpNames.Pascal(s) : CSharpNames.Pascal(s)));

    private static Body? ReadBody(
        string key, IOpenApiRequestBody? requestBody, string baseName, ClientModels models,
        HashSet<string> parameterNames, List<string> notes)
    {
        if (requestBody?.Content is not { Count: > 0 } content)
        {
            return null;
        }

        var name = CSharpNames.Unique("body", parameterNames);
        var json = content.FirstOrDefault(c => IsJson(c.Key));
        if (json.Key is not null)
        {
            return new Body(name, models.Map(json.Value.Schema, baseName + "Request"), true, requestBody.Required);
        }

        notes.Add($"{key}: the body is {content.Keys.First()}, so the method takes an HttpContent you build yourself.");
        return new Body(name, "HttpContent", false, requestBody.Required);
    }

    /// <summary>What the first success response holds: preferably 200, then 201, then any other 2xx.</summary>
    private static (ResultKind Kind, string? Type) ReadResult(OpenApiResponses? responses, string baseName, ClientModels models)
    {
        var success = (responses ?? [])
            .Where(r => r.Key.StartsWith('2'))
            .OrderBy(r => r.Key switch { "200" => 0, "201" => 1, _ => 2 })
            .Select(r => r.Value)
            .FirstOrDefault();

        if (success?.Content is not { Count: > 0 } content)
        {
            return (ResultKind.None, null);
        }

        var json = content.FirstOrDefault(c => IsJson(c.Key));
        if (json.Key is not null)
        {
            return (ResultKind.Json, models.Map(json.Value.Schema, baseName + "Response"));
        }

        return content.Keys.Any(k => k.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            ? (ResultKind.Text, "string")
            : (ResultKind.Bytes, "byte[]");
    }

    private static bool IsJson(string mediaType) =>
        mediaType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("+json", StringComparison.OrdinalIgnoreCase)
        || mediaType == "*/*";

    /// <summary>
    /// The scheme the chosen operations ask for. Bearer tokens (including OAuth2 and OpenID Connect access tokens)
    /// and API keys in a header are supported; the user pastes the token or key, the code doesn't fetch one.
    /// </summary>
    private static Auth? FindAuth(OpenApiDocument document, List<Operation> operations, List<string> notes)
    {
        var schemes = document.Components?.SecuritySchemes;
        if (schemes is not { Count: > 0 })
        {
            return null;
        }

        var requirements = (document.Paths ?? [])
            .SelectMany(p => (p.Value.Operations ?? []).Select(o => (Key: $"{o.Key.Method.ToUpperInvariant()} {p.Key}", Operation: o.Value)))
            .Where(o => operations.Any(chosen => chosen.Key == o.Key))
            .SelectMany(o => o.Operation.Security ?? document.Security ?? [])
            .SelectMany(requirement => requirement.Keys)
            .Select(reference => reference.Reference.Id)
            .OfType<string>()
            .Distinct()
            .ToList();

        foreach (var name in requirements)
        {
            if (!schemes.TryGetValue(name, out var scheme))
            {
                continue;
            }

            switch (scheme.Type)
            {
                case SecuritySchemeType.Http when string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase):
                    return new Auth("bearer", null, "a bearer token");
                case SecuritySchemeType.OAuth2 or SecuritySchemeType.OpenIdConnect:
                    notes.Add($"The API uses {scheme.Type} ('{name}'). The code sends an access token you get yourself; it doesn't sign in for you.");
                    return new Auth("bearer", null, "an access token");
                case SecuritySchemeType.ApiKey when scheme.In == ParameterLocation.Header && !string.IsNullOrWhiteSpace(scheme.Name):
                    return new Auth("header", scheme.Name, "an API key");
                default:
                    notes.Add($"Security scheme '{name}' ({scheme.Type} {scheme.Scheme ?? scheme.In?.ToString()}) isn't supported. Add it to the HttpClient yourself.");
                    break;
            }
        }

        return null;
    }

    /// <summary>The first server URL, with variables set to their defaults. It must end in a slash for relative paths to work.</summary>
    private static string BaseUrl(OpenApiDocument document, List<string> notes)
    {
        var server = document.Servers?.FirstOrDefault();
        var url = server?.Url;
        foreach (var (name, variable) in server?.Variables ?? new Dictionary<string, OpenApiServerVariable>())
        {
            url = url?.Replace("{" + name + "}", variable.Default);
        }

        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            notes.Add($"The specification has no full server URL, so the code uses {FallbackBaseUrl}. Change it to where the API runs.");
            return FallbackBaseUrl;
        }

        return url.EndsWith('/') ? url : url + "/";
    }

    // ---- ApiClient.cs ----

    private static string ClientFile(OpenApiDocument document, string root, List<Operation> operations)
    {
        var usesQuery = operations.Any(o => o.Parameters.Any(p => p.Location == ParameterLocation.Query));
        var usesText = usesQuery || operations.Any(o => o.Parameters.Count > 0);
        var usesJson = operations.Any(o => o.Body is { IsJson: true } || o.Result == ResultKind.Json);

        var code = new CodeWriter();
        if (usesText) code.Line("using System.Globalization;");
        code.Line("using System.Net;");
        if (usesJson)
        {
            code.Line("using System.Net.Http.Json;");
            code.Line("using System.Text.Json;");
            code.Line("using System.Text.Json.Serialization;");
        }

        code.Line();
        code.Line($"namespace {root};");
        code.Line();
        code.Line("/// <summary>");
        code.Line($"/// Calls {CSharpNames.XmlText(document.Info?.Title)} {CSharpNames.XmlText(document.Info?.Version)}. Generated by API Architect.");
        code.Line("/// The HttpClient's BaseAddress must be the API's address, ending in a slash.");
        code.Line("/// </summary>");
        code.Line($"public class {ClientName}(HttpClient httpClient)");
        code.Line("{");
        if (usesJson)
        {
            code.Line("    // camelCase names, case-insensitive reading, and no nulls sent for properties you leave empty.");
            code.Line("    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)");
            code.Line("    {");
            code.Line("        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,");
            code.Line("    };");
            code.Line();
        }

        foreach (var operation in operations)
        {
            WriteMethod(code, operation);
            code.Line();
        }

        WriteHelpers(code, usesText, usesQuery);
        code.Line("}");
        code.Line();
        code.Line("/// <summary>The API answered with an error status. Body is what it sent back, often a ProblemDetails JSON.</summary>");
        code.Line("public class ApiException(HttpStatusCode statusCode, string body)");
        code.Line("    : Exception($\"The API answered {(int)statusCode} {statusCode}.\")");
        code.Line("{");
        code.Line("    public HttpStatusCode StatusCode { get; } = statusCode;");
        code.Line();
        code.Line("    public string Body { get; } = body;");
        code.Line("}");
        return code.ToString();
    }

    private static void WriteMethod(CodeWriter code, Operation operation)
    {
        var arguments = new List<string>();
        foreach (var parameter in operation.Parameters.Where(p => p.Required))
        {
            arguments.Add($"{parameter.Type} {parameter.Name}");
        }

        if (operation.Body is { Required: true } requiredBody)
        {
            arguments.Add($"{requiredBody.Type} {requiredBody.Name}");
        }

        foreach (var parameter in operation.Parameters.Where(p => !p.Required))
        {
            arguments.Add($"{parameter.Type}? {parameter.Name} = null");
        }

        if (operation.Body is { Required: false } optionalBody)
        {
            arguments.Add($"{optionalBody.Type}? {optionalBody.Name} = null");
        }

        arguments.Add("CancellationToken cancellationToken = default");

        var returnType = operation.Result switch
        {
            ResultKind.None => "Task",
            ResultKind.Json when ClientModels.IsValueType(operation.ResultType!) => $"Task<{operation.ResultType}>",
            ResultKind.Json => $"Task<{operation.ResultType}?>",
            _ => $"Task<{operation.ResultType}>",
        };

        code.Summary(operation.Summary, indent: "    ");
        code.Line($"    /// <remarks>{CSharpNames.XmlText(operation.Key)}</remarks>");
        code.Line($"    public async {returnType} {operation.MethodName}({string.Join(", ", arguments)})");
        code.Line("    {");

        var query = operation.Parameters.Where(p => p.Location == ParameterLocation.Query).ToList();
        var path = PathExpression(operation);
        if (query.Count > 0)
        {
            code.Line("        var query = new List<string>();");
            foreach (var parameter in query)
            {
                code.Line($"        AddQuery(query, {CSharpNames.Literal(parameter.OriginalName)}, {parameter.Name});");
            }

            code.Line($"        var url = {path} + QueryString(query);");
        }
        else
        {
            code.Line($"        var url = {path};");
        }

        code.Line();
        code.Line($"        using var request = new HttpRequestMessage(HttpMethod.{CSharpNames.Pascal(operation.HttpMethod.ToLowerInvariant())}, url);");
        foreach (var header in operation.Parameters.Where(p => p.Location == ParameterLocation.Header))
        {
            var add = $"request.Headers.TryAddWithoutValidation({CSharpNames.Literal(header.OriginalName)}, Text({header.Name}));";
            code.Line(header.Required ? $"        {add}" : $"        if ({header.Name} is not null) {add}");
        }

        if (operation.Body is { } body)
        {
            var content = body.IsJson ? $"JsonContent.Create({body.Name}, options: JsonOptions)" : body.Name;
            code.Line(body.Required
                ? $"        request.Content = {content};"
                : $"        if ({body.Name} is not null) request.Content = {content};");
        }

        code.Line();
        code.Line("        using var response = await httpClient.SendAsync(request, cancellationToken);");
        code.Line("        await ThrowIfFailedAsync(response, cancellationToken);");
        switch (operation.Result)
        {
            case ResultKind.Json:
                code.Line($"        return await response.Content.ReadFromJsonAsync<{operation.ResultType}>(JsonOptions, cancellationToken);");
                break;
            case ResultKind.Text:
                code.Line("        return await response.Content.ReadAsStringAsync(cancellationToken);");
                break;
            case ResultKind.Bytes:
                code.Line("        return await response.Content.ReadAsByteArrayAsync(cancellationToken);");
                break;
        }

        code.Line("    }");
    }

    /// <summary><c>/pets/{id}</c> becomes <c>$"pets/{Path(id)}"</c>: relative to the BaseAddress, values escaped.</summary>
    private static string PathExpression(Operation operation)
    {
        var relative = operation.Path.TrimStart('/');
        var pathParameters = operation.Parameters.Where(p => p.Location == ParameterLocation.Path).ToList();
        if (pathParameters.Count == 0)
        {
            return CSharpNames.Literal(relative);
        }

        var text = relative.Replace("{", "{{").Replace("}", "}}");
        foreach (var parameter in pathParameters)
        {
            text = text.Replace("{{" + parameter.OriginalName + "}}", "{Path(" + parameter.Name + ")}");
        }

        return "$" + CSharpNames.Literal(text);
    }

    private static void WriteHelpers(CodeWriter code, bool usesText, bool usesQuery)
    {
        code.Line("    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken)");
        code.Line("    {");
        code.Line("        if (!response.IsSuccessStatusCode)");
        code.Line("        {");
        code.Line("            var body = await response.Content.ReadAsStringAsync(cancellationToken);");
        code.Line("            throw new ApiException(response.StatusCode, body);");
        code.Line("        }");
        code.Line("    }");

        if (!usesText)
        {
            return;
        }

        code.Line();
        code.Line("    /// <summary>A value as it goes in a URL or header: invariant culture, true/false, ISO dates.</summary>");
        code.Line("    private static string Text(object value) => value switch");
        code.Line("    {");
        code.Line("        bool b => b ? \"true\" : \"false\",");
        code.Line("        DateTimeOffset d => d.ToString(\"O\", CultureInfo.InvariantCulture),");
        code.Line("        DateOnly d => d.ToString(\"yyyy-MM-dd\", CultureInfo.InvariantCulture),");
        code.Line("        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),");
        code.Line("        _ => value.ToString() ?? \"\",");
        code.Line("    };");
        code.Line();
        code.Line("    private static string Path(object value) => Uri.EscapeDataString(Text(value));");

        if (!usesQuery)
        {
            return;
        }

        code.Line();
        code.Line("    /// <summary>Adds name=value, or name=a&amp;name=b for a list. Skips nulls.</summary>");
        code.Line("    private static void AddQuery(List<string> query, string name, object? value)");
        code.Line("    {");
        code.Line("        if (value is System.Collections.IEnumerable items and not string)");
        code.Line("        {");
        code.Line("            foreach (var item in items)");
        code.Line("            {");
        code.Line("                AddQuery(query, name, item);");
        code.Line("            }");
        code.Line("        }");
        code.Line("        else if (value is not null)");
        code.Line("        {");
        code.Line("            query.Add(Uri.EscapeDataString(name) + \"=\" + Uri.EscapeDataString(Text(value)));");
        code.Line("        }");
        code.Line("    }");
        code.Line();
        code.Line("    private static string QueryString(List<string> query) => query.Count == 0 ? \"\" : \"?\" + string.Join(\"&\", query);");
    }

    // ---- Example calls, shared by Program.cs and Worker.cs ----

    /// <summary>
    /// One call per operation. GETs without required values run as they are; the rest are commented out,
    /// because sending made-up values to a real API could change real data.
    /// </summary>
    private static void WriteCalls(CodeWriter code, List<Operation> operations, string indent, string token, Func<Operation, string, string> report)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal) { "api", "ex", "interval", "timer" };
        if (!operations.Any(IsRunnable))
        {
            code.Line($"{indent}// Nothing runs until you uncomment a call below.");
            code.Line($"{indent}await Task.CompletedTask;");
            code.Line();
        }

        var first = true;
        foreach (var operation in operations)
        {
            if (!first) code.Line();
            first = false;

            var runnable = IsRunnable(operation);
            var arguments = operation.Parameters.Where(p => p.Required).Select(p => $"{p.Name}: {ExampleValue(p.Type)}").ToList();
            if (operation.Body is { Required: true } body)
            {
                arguments.Add($"{body.Name}: {(body.IsJson ? ExampleValue(body.Type) : "new StringContent(\"\")")}");
            }

            arguments.Add($"cancellationToken: {token}");
            var call = $"await api.{operation.MethodName}({string.Join(", ", arguments)})";
            var prefix = runnable ? indent : indent + "// ";

            code.Line($"{indent}// {operation.Key}" + (runnable ? "" : ". Put in real values, then uncomment."));
            if (operation.Result == ResultKind.None)
            {
                code.Line($"{prefix}{call};");
                code.Line($"{prefix}{report(operation, "")}");
            }
            else
            {
                var variable = CSharpNames.Unique(CSharpNames.Camel(operation.MethodName[..^"Async".Length]), variables);
                code.Line($"{prefix}var {variable} = {call};");
                code.Line($"{prefix}{report(operation, variable)}");
            }
        }
    }

    private static bool IsRunnable(Operation operation) =>
        operation.HttpMethod == "GET" && !operation.Parameters.Any(p => p.Required) && operation.Body is not { Required: true };

    private static string ExampleValue(string type) => type switch
    {
        "string" => "\"value\"",
        "int" or "long" or "float" or "double" or "decimal" => "1",
        "bool" => "true",
        "Guid" => "Guid.NewGuid()",
        "DateTimeOffset" => "DateTimeOffset.UtcNow",
        "DateOnly" => "DateOnly.FromDateTime(DateTime.Today)",
        "TimeOnly" => "TimeOnly.FromDateTime(DateTime.Now)",
        "JsonElement" => "default",
        "byte[]" => "[]",
        _ => "new()",
    };

    // ---- Console App ----

    private static string ConsoleProgramFile(string root, List<Operation> operations, Auth? auth, string baseUrl)
    {
        var envVar = EnvironmentVariable(root);
        var code = new CodeWriter();
        if (auth?.Kind == "bearer") code.Line("using System.Net.Http.Headers;");
        code.Line("using System.Text.Json;");
        code.Line($"using {root};");
        code.Line();
        code.Line("// The API's address. Keep the slash at the end.");
        code.Line($"var baseUrl = new Uri({CSharpNames.Literal(baseUrl)});");
        code.Line();
        if (auth is not null)
        {
            code.Line($"// {Capitalize(auth.Description)} from an environment variable, so it never ends up in your code.");
            code.Line($"var apiKey = Environment.GetEnvironmentVariable({CSharpNames.Literal(envVar)});");
            code.Line("if (string.IsNullOrWhiteSpace(apiKey))");
            code.Line("{");
            code.Line($"    Console.WriteLine(\"Set the {envVar} environment variable first.\");");
            code.Line("    return;");
            code.Line("}");
            code.Line();
        }

        code.Line("using var httpClient = new HttpClient { BaseAddress = baseUrl };");
        WriteAuthHeader(code, auth, "httpClient", "");
        code.Line();
        code.Line("// Ctrl+C cancels the calls that are still running.");
        code.Line("using var cancellation = new CancellationTokenSource();");
        code.Line("Console.CancelKeyPress += (_, e) =>");
        code.Line("{");
        code.Line("    e.Cancel = true;");
        code.Line("    cancellation.Cancel();");
        code.Line("};");
        code.Line();
        code.Line($"var api = new {ClientName}(httpClient);");
        code.Line();
        code.Line("try");
        code.Line("{");
        WriteCalls(code, operations, "    ", "cancellation.Token", (operation, variable) => variable.Length == 0
            ? $"Console.WriteLine({CSharpNames.Literal(operation.Key + " done.")});"
            : $"Console.WriteLine(JsonSerializer.Serialize({variable}));");
        code.Line("}");
        code.Line("catch (ApiException ex)");
        code.Line("{");
        code.Line("    Console.WriteLine(ex.Message);");
        code.Line("    Console.WriteLine(ex.Body);");
        code.Line("}");
        code.Line("catch (HttpRequestException ex)");
        code.Line("{");
        code.Line("    Console.WriteLine($\"Couldn't reach the API: {ex.Message}\");");
        code.Line("}");
        return code.ToString();
    }

    private static List<string> ConsoleSteps(string root, bool hasModels, Auth? auth)
    {
        var steps = new List<string>
        {
            $"In Visual Studio, choose `File > New > Project`. Pick `Console App` (C#, not the one marked .NET Framework). Name it `{root}`.",
            "On the next page, choose `.NET 11` as the framework. Leave `Do not use top-level statements` unticked.",
            "No NuGet packages are needed.",
            $"Right-click the project and choose `Add > Class`. Name it `ApiClient.cs` and replace its contents with ApiClient.cs below.",
        };
        if (hasModels)
        {
            steps.Add("Do the same for `Models.cs`.");
        }

        steps.Add("Replace everything in `Program.cs` with Program.cs below. Check the address in `baseUrl`.");
        if (auth is not null)
        {
            steps.Add($"Add your key as an environment variable: `Project > {root} Properties > Debug > General > Open debug launch profiles UI`, then under `Environment variables` add `{EnvironmentVariable(root)}` with {auth.Description} as the value. This keeps it out of your code.");
        }

        steps.Add("Press F5. GET calls without required values run straight away. Uncomment the others once you've put in real values.");
        return steps;
    }

    // ---- Worker Service ----

    private static string WorkerProgramFile(string root, Auth? auth)
    {
        var code = new CodeWriter();
        if (auth?.Kind == "bearer") code.Line("using System.Net.Http.Headers;");
        code.Line($"using {root};");
        code.Line();
        code.Line("var builder = Host.CreateApplicationBuilder(args);");
        code.Line();
        code.Line(auth is null
            ? "// The address comes from appsettings.json."
            : "// The address comes from appsettings.json and the key from user secrets.");
        code.Line($"var baseUrl = builder.Configuration[\"{ConfigSection}:BaseUrl\"]");
        code.Line($"    ?? throw new InvalidOperationException(\"Add {ConfigSection}:BaseUrl to appsettings.json.\");");
        if (auth is not null)
        {
            code.Line($"var apiKey = builder.Configuration[\"{ConfigSection}:ApiKey\"]");
            code.Line($"    ?? throw new InvalidOperationException(\"Add {ConfigSection}:ApiKey with Manage User Secrets.\");");
        }

        code.Line();
        code.Line("// A named HttpClient. The factory reuses connections and picks up DNS changes.");
        code.Line($"builder.Services.AddHttpClient(\"{ConfigSection}\", client =>");
        code.Line("{");
        code.Line("    client.BaseAddress = new Uri(baseUrl);");
        WriteAuthHeader(code, auth, "client", "    ");
        code.Line("});");
        code.Line("builder.Services.AddHostedService<Worker>();");
        code.Line();
        code.Line("var host = builder.Build();");
        code.Line("host.Run();");
        return code.ToString();
    }

    private static string WorkerFile(string root, List<Operation> operations)
    {
        var code = new CodeWriter();
        code.Line($"namespace {root};");
        code.Line();
        code.Line($"/// <summary>Calls the API when the app starts, then every {ConfigSection}:IntervalSeconds until it stops.</summary>");
        code.Line("public class Worker(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<Worker> logger) : BackgroundService");
        code.Line("{");
        code.Line("    protected override async Task ExecuteAsync(CancellationToken stoppingToken)");
        code.Line("    {");
        code.Line($"        var interval = TimeSpan.FromSeconds(configuration.GetValue(\"{ConfigSection}:IntervalSeconds\", {DefaultIntervalSeconds}));");
        code.Line("        using var timer = new PeriodicTimer(interval);");
        code.Line();
        code.Line("        do");
        code.Line("        {");
        code.Line("            try");
        code.Line("            {");
        code.Line("                await RunOnceAsync(stoppingToken);");
        code.Line("            }");
        code.Line("            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)");
        code.Line("            {");
        code.Line("                // One failed run shouldn't stop the worker. Log it and try again next time.");
        code.Line("                logger.LogError(ex, \"Run failed. Trying again in {Interval}.\", interval);");
        code.Line("            }");
        code.Line("        }");
        code.Line("        while (await timer.WaitForNextTickAsync(stoppingToken));");
        code.Line("    }");
        code.Line();
        code.Line("    private async Task RunOnceAsync(CancellationToken cancellationToken)");
        code.Line("    {");
        code.Line($"        var api = new {ClientName}(httpClientFactory.CreateClient(\"{ConfigSection}\"));");
        code.Line();
        code.Line("        // Each call returns typed data. Replace the log lines with what you want to do with it.");
        code.Line();
        WriteCalls(code, operations, "        ", "cancellationToken", (operation, variable) =>
        {
            var name = CSharpNames.Literal(operation.Key);
            if (variable.Length > 0 && operation.ResultType?.StartsWith("List<", StringComparison.Ordinal) == true)
            {
                return $"logger.LogInformation(\"{{Operation}} returned {{Count}} items.\", {name}, {variable}?.Count ?? 0);";
            }

            return $"logger.LogInformation(\"{{Operation}} succeeded.\", {name});";
        });
        code.Line("    }");
        code.Line("}");
        return code.ToString();
    }

    private static string AppSettingsFile(string baseUrl) => $$"""
        {
          "Logging": {
            "LogLevel": {
              "Default": "Information",
              "Microsoft.Hosting.Lifetime": "Information",
              "System.Net.Http.HttpClient": "Warning"
            }
          },
          "{{ConfigSection}}": {
            "BaseUrl": {{System.Text.Json.JsonSerializer.Serialize(baseUrl)}},
            "IntervalSeconds": {{DefaultIntervalSeconds}}
          }
        }

        """;

    private static string SecretsFile() => $$"""
        {
          "{{ConfigSection}}": {
            "ApiKey": "paste-your-key-here"
          }
        }

        """;

    private static List<string> WorkerSteps(string root, bool hasModels, Auth? auth)
    {
        var steps = new List<string>
        {
            $"In Visual Studio, choose `File > New > Project`. Pick `Worker Service` (C#). Name it `{root}`.",
            "On the next page, choose `.NET 11` as the framework.",
            $"Right-click the project and choose `Manage NuGet Packages`. On `Browse`, find `{HttpPackage}` and install it. It gives you AddHttpClient.",
            "Right-click the project and choose `Add > Class`. Name it `ApiClient.cs` and replace its contents with ApiClient.cs below.",
        };
        if (hasModels)
        {
            steps.Add("Do the same for `Models.cs`.");
        }

        steps.Add("Replace everything in `Program.cs` and `Worker.cs` with the files below.");
        steps.Add($"Replace `appsettings.json` with the one below. Check `BaseUrl`, and set `IntervalSeconds` to how often it should run.");
        if (auth is not null)
        {
            steps.Add($"Right-click the project and choose `Manage User Secrets`. Paste secrets.json below and put in {auth.Description}. User secrets stay on your machine, outside the project folder.");
        }

        steps.Add("Press F5. It calls the API straight away, then on the interval. Stop it with Ctrl+C or Stop Debugging.");
        return steps;
    }

    // ---- Shared bits ----

    private static void WriteAuthHeader(CodeWriter code, Auth? auth, string client, string indent)
    {
        if (auth?.Kind == "bearer")
        {
            code.Line($"{indent}{client}.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(\"Bearer\", apiKey);");
        }
        else if (auth?.Kind == "header")
        {
            code.Line($"{indent}{client}.DefaultRequestHeaders.Add({CSharpNames.Literal(auth.HeaderName!)}, apiKey);");
        }
    }

    /// <summary><c>PetStore.Connector</c> becomes <c>PETSTORE_CONNECTOR_API_KEY</c>.</summary>
    private static string EnvironmentVariable(string root) => root.Replace('.', '_').ToUpperInvariant() + "_API_KEY";

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
