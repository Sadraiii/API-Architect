using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiArchitect.Api.Features.Execution;

/// <summary>One HTTP header. Headers are a list, not a dictionary, because a name may repeat.</summary>
public sealed record HeaderPair(
    [property: Required, StringLength(ExecutionRules.HeaderNameMaxLength)] string Name,
    [property: StringLength(ExecutionRules.HeaderValueMaxLength)] string Value);

[JsonConverter(typeof(JsonStringEnumConverter<AuthKind>))]
public enum AuthKind
{
    /// <summary><c>Authorization: Bearer {value}</c>.</summary>
    Bearer,

    /// <summary>A header named <see cref="RequestAuth.Name"/> with the key as its value.</summary>
    ApiKeyHeader,

    /// <summary>A query parameter named <see cref="RequestAuth.Name"/> with the key as its value.</summary>
    ApiKeyQuery,
}

/// <summary>
/// Credentials for the outbound request. Kept apart from the headers so the executor knows which values are
/// secret: history stores the kind and name but never the value.
/// </summary>
/// <param name="Kind">How the credential is sent.</param>
/// <param name="Value">The token or key.</param>
/// <param name="Name">Header or query parameter name. Required for the API key kinds.</param>
public sealed record RequestAuth(
    AuthKind Kind,
    [property: Required, StringLength(ExecutionRules.HeaderValueMaxLength)] string Value,
    [property: StringLength(ExecutionRules.HeaderNameMaxLength)] string? Name = null);

/// <summary>A fully resolved request, ready to send. Variables must already be substituted.</summary>
/// <param name="Method">GET, POST, PUT, PATCH or DELETE.</param>
/// <param name="Url">Absolute http or https URL, including any query string.</param>
/// <param name="Headers">Request headers. Content-Type here is used when <paramref name="ContentType"/> is null.</param>
/// <param name="Body">Request body text, sent as UTF-8.</param>
/// <param name="ContentType">Media type of the body, such as <c>application/json</c>.</param>
/// <param name="Auth">Credentials to apply.</param>
/// <param name="SecretValues">
/// Other secret values used to build this request (for example substituted environment secrets). The executor
/// ignores them; <see cref="ExecutionHistory"/> masks every occurrence in what it stores.
/// </param>
public sealed record OutboundRequest(
    string Method,
    Uri Url,
    IReadOnlyList<HeaderPair> Headers,
    string? Body = null,
    string? ContentType = null,
    RequestAuth? Auth = null,
    IReadOnlyCollection<string>? SecretValues = null);

/// <summary>Why a request produced no response. <see cref="Message"/> is safe to show to the user.</summary>
public sealed record ExecutionError(string Code, string Message);

/// <summary>The outcome of sending an <see cref="OutboundRequest"/>.</summary>
/// <param name="StatusCode">HTTP status, or null when no response arrived (see <paramref name="Error"/>).</param>
/// <param name="ReasonPhrase">Reason phrase from the status line, when the server sent one.</param>
/// <param name="Headers">Response and content headers, one pair per value.</param>
/// <param name="Body">Response body as text. Empty for binary content.</param>
/// <param name="BodyTruncated">True when the response was larger than the configured limit.</param>
/// <param name="BodyIsBinary">True when the content isn't text, so <paramref name="Body"/> is left empty.</param>
/// <param name="BodySizeBytes">Body bytes received (after decompression), up to the limit.</param>
/// <param name="DurationMs">Time from sending to the end of the body.</param>
/// <param name="Error">Set when the request failed before a complete response was read.</param>
public sealed record ExecutionResult(
    int? StatusCode,
    string? ReasonPhrase,
    IReadOnlyList<HeaderPair> Headers,
    string Body,
    bool BodyTruncated,
    bool BodyIsBinary,
    long BodySizeBytes,
    long DurationMs,
    ExecutionError? Error);

public static class ExecutionErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string BlockedAddress = "blocked_address";
    public const string DnsFailure = "dns_failure";
    public const string ConnectionFailed = "connection_failed";
    public const string TlsFailure = "tls_failure";
    public const string Timeout = "timeout";
    public const string InvalidResponse = "invalid_response";
    public const string RequestFailed = "request_failed";
}

public static class ExecutionRules
{
    public static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    public const int UrlMaxLength = 2048;
    public const int HeaderNameMaxLength = 256;
    public const int HeaderValueMaxLength = 8192;
    public const int MaxHeaders = 50;
    public const int ContentTypeMaxLength = 200;

    /// <summary>
    /// Headers users may not set: the HTTP stack owns framing and connection handling, and Host would let a
    /// request claim to be for a different site than the one it connects to.
    /// </summary>
    public static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Upgrade", "TE", "Trailer",
        "Expect", "Proxy-Authorization", "Proxy-Connection",
    };
}
