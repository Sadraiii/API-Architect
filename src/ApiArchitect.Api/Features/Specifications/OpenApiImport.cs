using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace ApiArchitect.Api.Features.Specifications;

/// <summary>
/// Checks an uploaded document with Microsoft.OpenApi (the library ASP.NET Core's own OpenAPI generation uses)
/// and pulls out the fields we keep alongside it.
/// </summary>
public static class OpenApiImport
{
    /// <summary>Enough to fix a document without flooding the response for a badly broken one.</summary>
    private const int MaxReportedErrors = 20;

    public sealed record Result(string Title, string Version, string OpenApiVersion);

    /// <summary>Parses <paramref name="content"/>. Returns the extracted fields, or the problems found.</summary>
    public static (Result? Result, string[] Errors) Read(string content)
    {
        var settings = new OpenApiReaderSettings
        {
            // Never follow external $refs. Resolving them would make the server fetch any URL a user puts in a
            // document (SSRF), and the stored document is what gets rendered anyway.
            LoadExternalRefs = false,
        };

        ReadResult read;
        try
        {
            read = OpenApiDocument.Parse(content, OpenApiConstants.Json, settings);
        }
        catch (Exception ex) when (ex is OpenApiException or System.Text.Json.JsonException or FormatException)
        {
            return (null, ["The content isn't a readable OpenAPI JSON document."]);
        }

        var diagnostic = read.Diagnostic;
        if (diagnostic?.Errors.Count > 0 || read.Document is null)
        {
            var errors = diagnostic?.Errors
                .Take(MaxReportedErrors)
                // JSON syntax errors carry a "#line=0" pointer that adds nothing; the message already has the position.
                .Select(e => string.IsNullOrEmpty(e.Pointer) || e.Pointer.StartsWith("#line", StringComparison.Ordinal)
                    ? e.Message
                    : $"{e.Message} (at {e.Pointer})")
                .ToArray();
            return (null, errors is { Length: > 0 } ? errors : ["The content isn't a readable OpenAPI JSON document."]);
        }

        if (diagnostic?.SpecificationVersion == OpenApiSpecVersion.OpenApi2_0)
        {
            return (null, ["Swagger 2.0 documents aren't supported. Convert it to OpenAPI 3 first, for example with Swagger Editor."]);
        }

        var document = read.Document;
        var title = document.Info?.Title;
        var version = document.Info?.Version;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(version))
        {
            return (null, ["The document needs info.title and info.version."]);
        }

        var openApiVersion = ReadOpenApiField(content);
        if (title.Length > SpecificationRules.TitleMaxLength
            || version.Length > SpecificationRules.VersionMaxLength
            || openApiVersion is null || openApiVersion.Length > SpecificationRules.OpenApiVersionMaxLength)
        {
            return (null, ["The document's info.title, info.version or openapi field is too long."]);
        }

        return (new Result(title.Trim(), version.Trim(), openApiVersion), []);
    }

    /// <summary>
    /// The literal <c>openapi</c> value (e.g. 3.1.1). The parsed document only exposes the major/minor
    /// line it was read as, and we want to show what the author wrote.
    /// </summary>
    private static string? ReadOpenApiField(string content)
    {
        using var json = System.Text.Json.JsonDocument.Parse(content);
        return json.RootElement.TryGetProperty("openapi", out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
