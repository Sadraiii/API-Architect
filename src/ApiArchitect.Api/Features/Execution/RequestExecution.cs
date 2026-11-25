using ApiArchitect.Api.Features.Projects;

namespace ApiArchitect.Api.Features.Execution;

/// <summary>
/// One sent request and its response, kept as history. Secrets are masked and bodies capped before saving,
/// and each project keeps only its newest executions (see <see cref="ExecutionOptions.HistoryLimitPerProject"/>).
/// </summary>
public sealed class RequestExecution
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProjectId { get; init; }

    public Project Project { get; init; } = null!;

    /// <summary>The saved request this ran from, or null for an ad-hoc request.</summary>
    public Guid? RequestId { get; init; }

    public required string Method { get; init; }

    /// <summary>The URL as the user wrote it (an API key sent in the query is not included), with secrets masked.</summary>
    public required string Url { get; init; }

    public List<HeaderPair> RequestHeaders { get; init; } = [];

    public string? RequestContentType { get; init; }

    public string? RequestBody { get; init; }

    public bool RequestBodyTruncated { get; init; }

    /// <summary>How auth was applied. The value itself is never stored.</summary>
    public AuthKind? AuthKind { get; init; }

    public string? AuthName { get; init; }

    public int? StatusCode { get; init; }

    public string? ReasonPhrase { get; init; }

    public List<HeaderPair> ResponseHeaders { get; init; } = [];

    public string ResponseBody { get; init; } = "";

    public bool ResponseBodyTruncated { get; init; }

    public bool ResponseBodyIsBinary { get; init; }

    public long ResponseSizeBytes { get; init; }

    public long DurationMs { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset ExecutedAt { get; init; }
}
