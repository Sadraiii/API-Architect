using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiArchitect.Api.Features.Designs;

/// <summary>
/// Generates an ASP.NET Core minimal API project (.NET 11) from a design, from fixed templates: the same design
/// always produces the same files. Models become records with DataAnnotations, each resource becomes an
/// endpoints class, and each endpoint becomes a typed handler stub for the developer to fill in.
/// </summary>
public static class CSharpProjectGenerator
{
    public sealed record GeneratedFile(string Path, string Content);

    // Package versions the generated project references. They match what this app itself uses.
    private const string AspNetCoreVersion = "11.0.0";
    private const string ScalarVersion = "3.2.1";

    private const int HttpPort = 5080;
    private const int HttpsPort = 7080;

    public static IReadOnlyList<GeneratedFile> Generate(DesignDocument design)
    {
        var root = design.Namespace;
        var hasAuth = design.Endpoints.Any(e => e.RequiresAuth);
        var resources = design.Endpoints.GroupBy(e => e.Resource, StringComparer.Ordinal).ToList();

        var files = new List<GeneratedFile>
        {
            new($"{root}/{root}.csproj", ProjectFile(hasAuth)),
            new($"{root}/Program.cs", ProgramFile(design, resources.Select(r => r.Key), hasAuth)),
        };

        if (hasAuth)
        {
            files.Add(new($"{root}/OpenApi/BearerSecurityTransformer.cs", BearerTransformerFile(root)));
        }

        files.AddRange(design.Models.Select(m => new GeneratedFile($"{root}/Models/{m.Name}.cs", ModelFile(root, m))));
        files.AddRange(resources.Select(r => new GeneratedFile($"{root}/Endpoints/{r.Key}Endpoints.cs", EndpointsFile(root, r.Key, r.ToList()))));
        files.Add(new($"{root}/Properties/launchSettings.json", LaunchSettingsFile()));
        files.Add(new($"{root}/appsettings.json", AppSettingsFile(includeAllowedHosts: true)));
        files.Add(new($"{root}/appsettings.Development.json", AppSettingsFile(includeAllowedHosts: false)));
        files.Add(new($"{root}/{root}.http", HttpFile(design)));
        files.Add(new($"{root}/README.md", ReadmeFile(design, hasAuth)));
        files.Add(new($"{root}/.gitignore", "bin/\nobj/\n.vs/\n*.user\n"));
        return files;
    }

    /// <summary>Packs the files into a zip, with a fixed timestamp so identical designs give identical archives.</summary>
    public static byte[] Zip(IEnumerable<GeneratedFile> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(file.Content);
            }
        }

        return stream.ToArray();
    }

    private static string ProjectFile(bool hasAuth)
    {
        var jwt = hasAuth
            ? $"\n    <PackageReference Include=\"Microsoft.AspNetCore.Authentication.JwtBearer\" Version=\"{AspNetCoreVersion}\" />"
            : "";
        return $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <!-- XML doc comments on models and handlers become descriptions in the OpenAPI document. -->
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>
              </PropertyGroup>

              <ItemGroup>{jwt}
                <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="{AspNetCoreVersion}" />
                <PackageReference Include="Scalar.AspNetCore" Version="{ScalarVersion}" />
              </ItemGroup>

            </Project>

            """;
    }

    private static string ProgramFile(DesignDocument design, IEnumerable<string> resources, bool hasAuth)
    {
        var code = new CodeWriter();
        code.Line($"using {design.Namespace}.Endpoints;");
        if (hasAuth) code.Line($"using {design.Namespace}.OpenApi;");
        code.Line("using Scalar.AspNetCore;");
        code.Line();
        code.Line("var builder = WebApplication.CreateBuilder(args);");
        code.Line();
        code.Line("// Errors are RFC 9457 ProblemDetails. AddValidation checks DataAnnotations on parameters and request");
        code.Line("// bodies before a handler runs and answers 400 with the problems it found.");
        code.Line("builder.Services.AddProblemDetails();");
        code.Line("builder.Services.AddValidation();");
        if (hasAuth)
        {
            code.Line("builder.Services.AddOpenApi(options => options.AddBearerSecurity());");
            code.Line();
            code.Line("// JWT bearer tokens. Settings come from the Authentication:Schemes:Bearer configuration section;");
            code.Line("// for local testing, `dotnet user-jwts create` fills it in and prints a token.");
            code.Line("builder.Services.AddAuthentication().AddJwtBearer();");
            code.Line("builder.Services.AddAuthorization();");
        }
        else
        {
            code.Line("builder.Services.AddOpenApi();");
        }

        code.Line();
        code.Line("// Handlers that still throw NotImplementedException answer 501 Not Implemented rather than 500.");
        code.Line("builder.Services.Configure<ExceptionHandlerOptions>(options =>");
        code.Line("    options.StatusCodeSelector = exception => exception is NotImplementedException");
        code.Line("        ? StatusCodes.Status501NotImplemented");
        code.Line("        : StatusCodes.Status500InternalServerError);");
        code.Line();
        code.Line("var app = builder.Build();");
        code.Line();
        code.Line("app.UseExceptionHandler();");
        code.Line("app.UseStatusCodePages();");
        code.Line();
        code.Line("if (app.Environment.IsDevelopment())");
        code.Line("{");
        code.Line("    // The OpenAPI document at /openapi/v1.json and an API reference at /scalar.");
        code.Line("    app.MapOpenApi();");
        code.Line("    app.MapScalarApiReference();");
        code.Line("}");
        code.Line();
        code.Line("app.UseHttpsRedirection();");
        if (hasAuth)
        {
            code.Line("app.UseAuthentication();");
            code.Line("app.UseAuthorization();");
        }

        code.Line();
        foreach (var resource in resources)
        {
            code.Line($"app.Map{resource}Endpoints();");
        }

        code.Line();
        code.Line("app.Run();");
        return code.ToString();
    }

    private static string BearerTransformerFile(string root) => $$"""
        using Microsoft.AspNetCore.Authorization;
        using Microsoft.AspNetCore.OpenApi;
        using Microsoft.OpenApi;

        namespace {{root}}.OpenApi;

        /// <summary>
        /// Describes the JWT bearer scheme in the OpenAPI document and marks the operations that need it,
        /// so Scalar shows where to paste a token.
        /// </summary>
        public static class BearerSecurityTransformer
        {
            private const string SchemeName = "Bearer";

            public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options)
            {
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Components ??= new OpenApiComponents();
                    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                    document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        Description = "A JWT access token in the Authorization header.",
                    };
                    return Task.CompletedTask;
                });

                options.AddOperationTransformer((operation, context, cancellationToken) =>
                {
                    var requiresAuth = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any();
                    if (requiresAuth)
                    {
                        operation.Security ??= [];
                        operation.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
                        });
                    }

                    return Task.CompletedTask;
                });

                return options;
            }
        }

        """;

    private static string ModelFile(string root, ModelDefinition model)
    {
        var code = new CodeWriter();
        var properties = model.Properties.Select(p => (Property: p, Attributes: Attributes(p))).ToList();
        if (properties.Any(p => p.Attributes.Count > 0))
        {
            code.Line("using System.ComponentModel.DataAnnotations;");
            code.Line();
        }

        code.Line($"namespace {root}.Models;");
        code.Line();
        code.DocSummary(model.Description);
        foreach (var property in model.Properties.Where(p => !string.IsNullOrWhiteSpace(p.Description)))
        {
            code.Line($"/// <param name=\"{property.Name}\">{XmlEscape(OneLine(property.Description!))}</param>");
        }

        if (properties.Count == 0)
        {
            code.Line($"public sealed record {model.Name};");
            return code.ToString();
        }

        code.Line($"public sealed record {model.Name}(");
        for (var i = 0; i < properties.Count; i++)
        {
            var (property, attributes) = properties[i];
            var prefix = attributes.Count > 0 ? $"[{string.Join(", ", attributes)}] " : "";
            var separator = i == properties.Count - 1 ? ");" : ",";
            code.Line($"    {prefix}{PropertyType(property)} {property.Name}{separator}");
        }

        return code.ToString();
    }

    /// <summary>The C# type: non-nullable when required, nullable (<c>string?</c>, <c>int?</c>) when optional.</summary>
    private static string PropertyType(PropertyDefinition property)
    {
        var type = DesignTypes.Simple.TryGetValue(property.Type, out var simple) ? simple.CSharp : property.Type;
        if (property.IsList)
        {
            type = $"List<{type}>";
        }

        return property.Required ? type : $"{type}?";
    }

    private static List<string> Attributes(PropertyDefinition property)
    {
        var attributes = new List<string>();
        DesignTypes.Simple.TryGetValue(property.Type, out var simple);
        var isValueType = simple?.IsValueType == true && !property.IsList;

        // [Required] does nothing on a non-nullable value type: a missing int is simply 0.
        if (property.Required && !isValueType)
        {
            attributes.Add("Required");
        }

        if (property.MaxLength is { } max)
        {
            attributes.Add(property.MinLength is { } min and > 0 ? $"StringLength({max}, MinimumLength = {min})" : $"StringLength({max})");
        }
        else if (property.MinLength is { } min and > 0)
        {
            attributes.Add($"MinLength({min})");
        }

        if (property.Minimum is not null || property.Maximum is not null)
        {
            attributes.Add(RangeAttribute(property));
        }

        if (property.Pattern is not null)
        {
            attributes.Add($"RegularExpression(@\"{property.Pattern.Replace("\"", "\"\"")}\")");
        }

        if (property.Format == "email") attributes.Add("EmailAddress");
        if (property.Format == "url") attributes.Add("Url");
        return attributes;
    }

    /// <summary>
    /// [Range] with int bounds for int properties when they fit, otherwise with double bounds, which
    /// RangeAttribute compares against long, decimal and double values alike.
    /// </summary>
    private static string RangeAttribute(PropertyDefinition property)
    {
        if (property.Type == "int"
            && property.Minimum is null or { Scale: 0 } and >= int.MinValue and <= int.MaxValue
            && property.Maximum is null or { Scale: 0 } and >= int.MinValue and <= int.MaxValue)
        {
            var min = property.Minimum is { } a ? ((int)a).ToString(CultureInfo.InvariantCulture) : "int.MinValue";
            var max = property.Maximum is { } b ? ((int)b).ToString(CultureInfo.InvariantCulture) : "int.MaxValue";
            return $"Range({min}, {max})";
        }

        var low = property.Minimum is { } c ? DoubleLiteral(c) : "double.MinValue";
        var high = property.Maximum is { } d ? DoubleLiteral(d) : "double.MaxValue";
        return $"Range({low}, {high})";
    }

    private static string DoubleLiteral(decimal value) => $"{value.ToString(CultureInfo.InvariantCulture)}d";

    private static string EndpointsFile(string root, string resource, List<EndpointDefinition> endpoints)
    {
        var prefix = CommonPrefix(endpoints.Select(e => e.Route));
        var code = new CodeWriter();
        code.Line("using Microsoft.AspNetCore.Http.HttpResults;");
        if (endpoints.Any(e => e.RequestModel is not null || e.ResponseModel is not null))
        {
            code.Line($"using {root}.Models;");
        }

        code.Line();
        code.Line($"namespace {root}.Endpoints;");
        code.Line();
        code.Line($"public static class {resource}Endpoints");
        code.Line("{");
        code.Line($"    public static IEndpointRouteBuilder Map{resource}Endpoints(this IEndpointRouteBuilder app)");
        code.Line("    {");
        code.Line($"        var group = app.MapGroup(\"{prefix}\").WithTags(\"{resource}\");");
        foreach (var endpoint in endpoints)
        {
            var method = endpoint.Method[0] + endpoint.Method[1..].ToLowerInvariant();
            code.Line();
            code.Line($"        group.Map{method}(\"{endpoint.Route[prefix.Length..]}\", {endpoint.Name})");
            code.Line($"            .WithName(\"{endpoint.Name}\")");
            if (!string.IsNullOrWhiteSpace(endpoint.Summary))
            {
                code.Line($"            .WithSummary({CSharpString(OneLine(endpoint.Summary))})");
            }

            if (endpoint.RequiresAuth)
            {
                code.Line("            .RequireAuthorization()");
            }

            code.AppendToLastLine(";");
        }

        code.Line();
        code.Line("        return app;");
        code.Line("    }");

        foreach (var endpoint in endpoints)
        {
            code.Line();
            Handler(code, endpoint);
        }

        code.Line("}");
        return code.ToString();
    }

    private static void Handler(CodeWriter code, EndpointDefinition endpoint)
    {
        var results = ResultTypes(endpoint);
        var returnType = results.Count == 1 ? results[0].Type : $"Results<{string.Join(", ", results.Select(r => r.Type))}>";

        var parameters = endpoint.Parameters
            .OrderBy(p => p.In == "path" ? 0 : 1)
            .Select(p => $"{DesignTypes.Simple[p.Type].CSharp}{(p.In == "path" || p.Required ? "" : "?")} {p.Name}")
            .ToList();
        if (endpoint.RequestModel is not null)
        {
            parameters.Add($"{endpoint.RequestModel} request");
        }

        parameters.Add("CancellationToken cancellationToken");

        code.Indent = 1;
        code.DocSummary(endpoint.Summary);
        code.Line($"private static {returnType} {endpoint.Name}({string.Join(", ", parameters)})");
        code.Line("{");
        code.Line($"    // TODO: Implement {endpoint.Name}. Return one of: {string.Join(", ", results.Select(r => r.Example))}.");
        code.Line("    // Make the handler async (Task<...>) once it awaits a database or another service.");
        code.Line("    throw new NotImplementedException();");
        code.Line("}");
        code.Indent = 0;
    }

    /// <summary>The TypedResults types for the endpoint's statuses, success first, with an example call for each.</summary>
    private static List<(string Type, string Example)> ResultTypes(EndpointDefinition endpoint)
    {
        var model = endpoint.ResponseModel is null ? null : endpoint.ResponseIsList ? $"List<{endpoint.ResponseModel}>" : endpoint.ResponseModel;
        var value = endpoint.ResponseModel is null ? null : endpoint.ResponseIsList ? "items" : ToCamel(endpoint.ResponseModel);

        var results = new List<(string, string)>
        {
            endpoint.SuccessStatus switch
            {
                200 when model is not null => ($"Ok<{model}>", $"TypedResults.Ok({value})"),
                200 => ("Ok", "TypedResults.Ok()"),
                201 when model is not null => ($"Created<{model}>", $"TypedResults.Created(location, {value})"),
                201 => ("Created", "TypedResults.Created(location)"),
                202 when model is not null => ($"Accepted<{model}>", $"TypedResults.Accepted(location, {value})"),
                202 => ("Accepted", "TypedResults.Accepted(location)"),
                _ => ("NoContent", "TypedResults.NoContent()"),
            },
        };

        foreach (var status in endpoint.ErrorStatuses.Order())
        {
            results.Add(status switch
            {
                400 => ("ValidationProblem", "TypedResults.ValidationProblem(errors)"),
                401 => ("UnauthorizedHttpResult", "TypedResults.Unauthorized()"),
                403 => ("ForbidHttpResult", "TypedResults.Forbid()"),
                404 => ("NotFound", "TypedResults.NotFound()"),
                _ => ("Conflict", "TypedResults.Conflict()"),
            });
        }

        return results;
    }

    /// <summary>The literal segments every route of a resource starts with, such as <c>/api/books</c>.</summary>
    private static string CommonPrefix(IEnumerable<string> routes)
    {
        string[]? common = null;
        foreach (var route in routes)
        {
            var segments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
            common = common is null
                ? segments.TakeWhile(s => !s.StartsWith('{')).ToArray()
                : common.Zip(segments).TakeWhile(pair => pair.First == pair.Second).Select(pair => pair.First).ToArray();
        }

        return common is { Length: > 0 } ? "/" + string.Join('/', common) : "";
    }

    private static string LaunchSettingsFile() => $$"""
        {
          "$schema": "https://json.schemastore.org/launchsettings.json",
          "profiles": {
            "http": {
              "commandName": "Project",
              "launchBrowser": true,
              "launchUrl": "scalar",
              "applicationUrl": "http://localhost:{{HttpPort}}",
              "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
            },
            "https": {
              "commandName": "Project",
              "launchBrowser": true,
              "launchUrl": "scalar",
              "applicationUrl": "https://localhost:{{HttpsPort}};http://localhost:{{HttpPort}}",
              "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
            }
          }
        }

        """;

    private static string AppSettingsFile(bool includeAllowedHosts)
    {
        var settings = new JsonObject
        {
            ["Logging"] = new JsonObject
            {
                ["LogLevel"] = new JsonObject { ["Default"] = "Information", ["Microsoft.AspNetCore"] = "Warning" },
            },
        };
        if (includeAllowedHosts)
        {
            settings["AllowedHosts"] = "*";
        }

        return settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>A .http file (Visual Studio, VS Code REST Client, Rider) with one sample request per endpoint.</summary>
    private static string HttpFile(DesignDocument design)
    {
        var models = design.Models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"@baseUrl = https://localhost:{HttpsPort}\n");
        if (design.Endpoints.Any(e => e.RequiresAuth))
        {
            text.Append("# Create a token with `dotnet user-jwts create` and paste it here.\n@token = \n");
        }

        foreach (var endpoint in design.Endpoints)
        {
            var route = DesignValidator.RouteParameter().Replace(endpoint.Route, match =>
            {
                var parameter = endpoint.Parameters.First(p => p.In == "path" && p.Name == match.Groups[1].Value);
                return Uri.EscapeDataString(SampleValue(parameter.Type, null).ToString()!.Trim('"'));
            });

            var query = endpoint.Parameters.Where(p => p.In == "query" && p.Required)
                .Select(p => $"{p.Name}={Uri.EscapeDataString(SampleValue(p.Type, null).ToString()!.Trim('"'))}")
                .ToList();

            text.Append(CultureInfo.InvariantCulture, $"\n### {OneLine(endpoint.Summary ?? endpoint.Name)}\n");
            text.Append(CultureInfo.InvariantCulture, $"{endpoint.Method} {{{{baseUrl}}}}{route}{(query.Count > 0 ? "?" + string.Join('&', query) : "")}\n");
            if (endpoint.RequiresAuth)
            {
                text.Append("Authorization: Bearer {{token}}\n");
            }

            if (endpoint.RequestModel is not null)
            {
                var body = SampleModel(models[endpoint.RequestModel], models, depth: 0);
                text.Append("Content-Type: application/json\n\n");
                text.Append(body.ToJsonString(new JsonSerializerOptions { WriteIndented = true })).Append('\n');
            }
        }

        return text.ToString();
    }

    private static JsonObject SampleModel(ModelDefinition model, Dictionary<string, ModelDefinition> models, int depth)
    {
        var sample = new JsonObject();
        foreach (var property in model.Properties)
        {
            JsonNode? value;
            if (models.TryGetValue(property.Type, out var nested))
            {
                // Stop at three levels so self-referencing models (a Category with child Categories) terminate.
                value = depth < 2 ? SampleModel(nested, models, depth + 1) : null;
            }
            else
            {
                value = SampleValue(property.Type, property);
            }

            sample[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = property.IsList
                ? value is null ? new JsonArray() : new JsonArray(value)
                : value;
        }

        return sample;
    }

    private static JsonNode SampleValue(string type, PropertyDefinition? property) => type switch
    {
        "string" when property?.Format == "email" => "user@example.com",
        "string" when property?.Format == "url" => "https://example.com",
        "string" => property is null ? "value" : JsonNamingPolicy.CamelCase.ConvertName(property.Name),
        "int" or "long" => (long)Math.Max(property?.Minimum ?? 1, Math.Min(property?.Maximum ?? 1, 1)),
        "decimal" or "double" => Math.Max(property?.Minimum ?? 1, Math.Min(property?.Maximum ?? 1, 1)),
        "bool" => true,
        "guid" => "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "datetime" => "2026-01-01T12:00:00Z",
        _ => "2026-01-01",
    };

    private static string ReadmeFile(DesignDocument design, bool hasAuth)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# {design.Title}\n\n");
        if (!string.IsNullOrWhiteSpace(design.Description))
        {
            text.Append(design.Description.Trim()).Append("\n\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"""
            ASP.NET Core minimal API (.NET 11), generated by API Architect from the design "{design.Title}" v{design.Version}.

            Run it with `dotnet run --launch-profile https` and open https://localhost:{HttpsPort}/scalar.
            `{design.Namespace}.http` has a sample request for each endpoint.

            Every handler in `Endpoints/` is a stub that returns 501 Not Implemented until you write it.
            Validation rules on the records in `Models/` are already enforced (invalid input gets a 400).

            """);

        if (hasAuth)
        {
            text.Append("""
                Endpoints that need auth expect a JWT bearer token. For local testing, `dotnet user-jwts create` prints one.

                """);
        }

        text.Append("\n## Endpoints\n\n| Method | Route | Name | Auth |\n| --- | --- | --- | --- |\n");
        foreach (var endpoint in design.Endpoints)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {endpoint.Method} | `{endpoint.Route}` | {endpoint.Name} | {(endpoint.RequiresAuth ? "Yes" : "No")} |\n");
        }

        return text.ToString();
    }

    private static string ToCamel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string OneLine(string value) => string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string XmlEscape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>A C# string literal that's safe for any input: JSON string escaping is valid C# escaping.</summary>
    private static string CSharpString(string value) => JsonSerializer.Serialize(value, CSharpStringOptions);

    private static readonly JsonSerializerOptions CSharpStringOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Line-by-line source writer with four-space indentation.</summary>
    private sealed class CodeWriter
    {
        private readonly List<string> _lines = [];

        public int Indent { get; set; }

        public void Line(string text = "") => _lines.Add(text.Length == 0 ? "" : new string(' ', Indent * 4) + text);

        public void AppendToLastLine(string text) => _lines[^1] += text;

        public void DocSummary(string? text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                Line($"/// <summary>{XmlEscape(OneLine(text))}</summary>");
            }
        }

        public override string ToString() => string.Join('\n', _lines) + "\n";
    }
}
