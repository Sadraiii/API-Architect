using System.ComponentModel.DataAnnotations;

namespace ApiArchitect.Api.Features.Execution;

/// <summary>Body for sending an ad-hoc request.</summary>
/// <param name="Method">GET, POST, PUT, PATCH or DELETE (upper case).</param>
/// <param name="Url">Absolute http or https URL, including the query string.</param>
/// <param name="Headers">Request headers. Host, Content-Length, Connection and other framing headers aren't allowed.</param>
/// <param name="Body">Request body text, sent as UTF-8. Up to 1 MB by default.</param>
/// <param name="ContentType">Media type of the body, such as application/json. Overrides a Content-Type header.</param>
/// <param name="Auth">Bearer token or API key. Stored history keeps the kind and name, never the value.</param>
public sealed record ExecuteRequest(
    [Required, AllowedValues("GET", "POST", "PUT", "PATCH", "DELETE")] string Method,
    [Required, StringLength(ExecutionRules.UrlMaxLength)] string Url,
    [MaxLength(ExecutionRules.MaxHeaders)] List<HeaderPair>? Headers,
    string? Body,
    [StringLength(ExecutionRules.ContentTypeMaxLength)] string? ContentType,
    RequestAuth? Auth);

/// <summary>How a request was authenticated, without the secret.</summary>
public sealed record AuthSummary(AuthKind Kind, string? Name);

/// <summary>A sent request and its outcome.</summary>
/// <remarks>
/// Straight after sending, <see cref="ResponseBody"/> is the live body (up to the response limit). Read back from
/// history it is the stored copy, which is capped smaller; <see cref="ResponseBodyTruncated"/> says which applies.
/// </remarks>
public sealed record ExecutionResponse(
    Guid Id,
    Guid? RequestId,
    string Method,
    string Url,
    IReadOnlyList<HeaderPair> RequestHeaders,
    string? RequestContentType,
    string? RequestBody,
    bool RequestBodyTruncated,
    AuthSummary? Auth,
    int? StatusCode,
    string? ReasonPhrase,
    IReadOnlyList<HeaderPair> ResponseHeaders,
    string ResponseBody,
    bool ResponseBodyTruncated,
    bool ResponseBodyIsBinary,
    long ResponseSizeBytes,
    long DurationMs,
    ExecutionError? Error,
    DateTimeOffset ExecutedAt);

/// <summary>A history entry, for lists.</summary>
public sealed record ExecutionSummary(
    Guid Id,
    Guid? RequestId,
    string Method,
    string Url,
    int? StatusCode,
    long DurationMs,
    string? ErrorCode,
    DateTimeOffset ExecutedAt);
