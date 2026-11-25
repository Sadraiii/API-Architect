using System.Linq.Expressions;
using System.Text;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiArchitect.Api.Features.Execution;

/// <summary>Saves executions with secrets masked and bodies capped, and keeps each project's history bounded.</summary>
public sealed class ExecutionHistory(AppDbContext db, TimeProvider clock, IOptions<ExecutionOptions> options)
{
    public const string Mask = "••••••";

    /// <summary>Header names whose values are always credentials, whatever the user's secrets are.</summary>
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "Api-Key", "X-Auth-Token",
    };

    private readonly ExecutionOptions _options = options.Value;

    /// <summary>
    /// Stores one execution and prunes the project's history to the configured limit. The caller must already
    /// have checked that the current user owns <paramref name="projectId"/>.
    /// </summary>
    /// <returns>The stored execution, but carrying the full live response body rather than the capped copy.</returns>
    public async Task<ExecutionResponse> RecordAsync(
        Guid projectId, Guid? requestId, OutboundRequest request, ExecutionResult result, CancellationToken cancellationToken)
    {
        var masker = new SecretMasker(request);
        var (requestBody, requestBodyTruncated) = Cap(masker.Apply(request.Body));
        var (responseBody, responseBodyTruncated) = Cap(masker.Apply(result.Body));

        var execution = new RequestExecution
        {
            ProjectId = projectId,
            RequestId = requestId,
            Method = request.Method,
            Url = masker.Apply(request.Url.OriginalString)!,
            RequestHeaders = MaskHeaders(request.Headers, masker, request.Auth),
            RequestContentType = request.ContentType,
            RequestBody = requestBody,
            RequestBodyTruncated = requestBodyTruncated,
            AuthKind = request.Auth?.Kind,
            AuthName = request.Auth?.Name,
            StatusCode = result.StatusCode,
            ReasonPhrase = result.ReasonPhrase,
            ResponseHeaders = MaskHeaders(result.Headers, masker, request.Auth),
            ResponseBody = responseBody ?? "",
            ResponseBodyTruncated = result.BodyTruncated || responseBodyTruncated,
            ResponseBodyIsBinary = result.BodyIsBinary,
            ResponseSizeBytes = result.BodySizeBytes,
            DurationMs = result.DurationMs,
            ErrorCode = result.Error?.Code,
            ErrorMessage = result.Error?.Message,
            ExecutedAt = clock.GetUtcNowForStorage(),
        };

        db.RequestExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);

        await db.RequestExecutions
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.ExecutedAt).ThenByDescending(e => e.Id)
            .Skip(_options.HistoryLimitPerProject)
            .ExecuteDeleteAsync(cancellationToken);

        // The caller is looking at this response right now, so give them all of it, masked like the stored copy.
        return MapToResponse(execution) with
        {
            ResponseBody = masker.Apply(result.Body)!,
            ResponseBodyTruncated = result.BodyTruncated,
        };
    }

    private (string? Text, bool Truncated) Cap(string? text)
    {
        var limit = _options.MaxStoredBodyBytes;
        if (text is null || Encoding.UTF8.GetByteCount(text) <= limit)
        {
            return (text, false);
        }

        // Cut on a character boundary: decoding a byte prefix may end in a partial character, which becomes U+FFFD.
        var prefix = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text), 0, limit);
        return (prefix.TrimEnd('�'), true);
    }

    private static List<HeaderPair> MaskHeaders(IEnumerable<HeaderPair> headers, SecretMasker masker, RequestAuth? auth) =>
    [
        .. headers.Select(h =>
            SensitiveHeaders.Contains(h.Name) || (auth?.Kind == AuthKind.ApiKeyHeader && h.Name.Equals(auth.Name, StringComparison.OrdinalIgnoreCase))
                ? h with { Value = Mask }
                : h with { Value = masker.Apply(h.Value)! }),
    ];

    public static readonly Expression<Func<RequestExecution, ExecutionResponse>> ToResponse = e => new ExecutionResponse(
        e.Id,
        e.RequestId,
        e.Method,
        e.Url,
        e.RequestHeaders,
        e.RequestContentType,
        e.RequestBody,
        e.RequestBodyTruncated,
        e.AuthKind == null ? null : new AuthSummary(e.AuthKind.Value, e.AuthName),
        e.StatusCode,
        e.ReasonPhrase,
        e.ResponseHeaders,
        e.ResponseBody,
        e.ResponseBodyTruncated,
        e.ResponseBodyIsBinary,
        e.ResponseSizeBytes,
        e.DurationMs,
        e.ErrorCode == null ? null : new ExecutionError(e.ErrorCode, e.ErrorMessage ?? ""),
        e.ExecutedAt);

    // The same mapping for an entity in memory. Declared after ToResponse so it initializes second.
    private static readonly Func<RequestExecution, ExecutionResponse> MapToResponse = ToResponse.Compile();

    /// <summary>Replaces every occurrence of a secret value, raw or URL-encoded, with <see cref="Mask"/>.</summary>
    private sealed class SecretMasker
    {
        private readonly string[] _secrets;

        public SecretMasker(OutboundRequest request)
        {
            IEnumerable<string> values = request.SecretValues ?? [];
            if (request.Auth is { } auth)
            {
                values = values.Append(auth.Value);
            }

            _secrets =
            [
                .. values
                    .Where(v => !string.IsNullOrEmpty(v))
                    .SelectMany(v => new[] { v, Uri.EscapeDataString(v) })
                    .Distinct()
                    // Longest first, so a secret that contains another is masked whole.
                    .OrderByDescending(v => v.Length),
            ];
        }

        public string? Apply(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            foreach (var secret in _secrets)
            {
                text = text.Replace(secret, Mask, StringComparison.Ordinal);
            }

            return text;
        }
    }
}
