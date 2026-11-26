using ApiArchitect.Api.Features.Execution;

namespace ApiArchitect.Api.Features.Specifications;

/// <summary>
/// Downloads an OpenAPI document from a URL, such as a local API's <c>/openapi/v1.json</c>.
/// </summary>
/// <remarks>
/// Goes through <see cref="RequestExecutor"/> so it gets the same protection as user requests: cloud metadata and
/// reserved addresses are always refused (localhost is allowed in the local default), redirects aren't followed,
/// and the executor's time limit and response size cap apply.
/// </remarks>
public sealed class SpecificationFetcher(RequestExecutor executor)
{
    /// <summary>Returns the document text, or a message that is safe to show the user.</summary>
    public async Task<(string? Content, string? Error)> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return (null, "Enter an absolute http:// or https:// URL.");
        }

        var result = await executor.ExecuteAsync(
            new OutboundRequest("GET", uri, [new HeaderPair("Accept", "application/json")]), cancellationToken);

        if (result.Error is { } error)
        {
            return (null, error.Message);
        }

        if (result.StatusCode is >= 300 and < 400)
        {
            var location = result.Headers.FirstOrDefault(h => h.Name.Equals("Location", StringComparison.OrdinalIgnoreCase))?.Value;
            return (null, location is null
                ? $"The URL answered with a redirect ({result.StatusCode}). Use the final URL instead."
                : $"The URL redirects to {location}. Use that URL instead.");
        }

        if (result.StatusCode is not (>= 200 and < 300))
        {
            return (null, $"The URL answered {result.StatusCode} {result.ReasonPhrase}".TrimEnd() + ", not a document.");
        }

        if (result.BodyTruncated || result.Body.Length > SpecificationRules.ContentMaxLength)
        {
            return (null, "The document is too large to import.");
        }

        if (result.BodyIsBinary || string.IsNullOrWhiteSpace(result.Body))
        {
            return (null, "The URL didn't return a JSON document.");
        }

        return (result.Body, null);
    }
}
