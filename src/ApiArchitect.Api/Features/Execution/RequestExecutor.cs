using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ApiArchitect.Api.Features.Execution;

/// <summary>
/// Sends user-defined HTTP requests from the server, safely: internal addresses are refused at connect time,
/// redirects are not followed, every request has a time limit, and response bodies are capped.
/// Registered as a singleton so its connection pool is shared.
/// </summary>
public sealed partial class RequestExecutor : IDisposable
{
    private const string DefaultUserAgent = "ApiArchitect/1.0";

    private readonly HttpClient _client;
    private readonly ExecutionOptions _options;
    private readonly ILogger<RequestExecutor> _logger;

    public RequestExecutor(IOptions<ExecutionOptions> options, ILogger<RequestExecutor> logger)
    {
        _options = options.Value;
        _logger = logger;

        var policy = new OutboundAddressPolicy(_options.AllowedNetworks.Select(IPNetwork.Parse));
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = policy.ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds),
            // A redirect is a new request to a URL the user didn't choose. Show it instead of following it.
            AllowAutoRedirect = false,
            // A proxy would make the connect callback check the proxy's address instead of the target's.
            UseProxy = false,
            // The pool is shared by every user, so nothing may carry state from one request to the next.
            UseCookies = false,
            Credentials = null,
            AutomaticDecompression = DecompressionMethods.All,
            // Re-resolve DNS for long-lived pools now and then.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        // Deliberately not IHttpClientFactory: its default logging handlers write full URLs, which can carry
        // API keys in the query string.
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// Checks a request before sending it. Returns errors keyed by field, in the shape ValidationProblem expects;
    /// empty means valid. <see cref="ExecuteAsync"/> runs this too.
    /// </summary>
    public IDictionary<string, string[]> Validate(OutboundRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = [.. errors.GetValueOrDefault(field, []), message];

        if (!ExecutionRules.Methods.Contains(request.Method))
        {
            Add("method", $"Method must be one of {string.Join(", ", ExecutionRules.Methods)}.");
        }

        var url = request.Url;
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            Add("url", "URL must be an absolute http:// or https:// address.");
        }
        else if (!string.IsNullOrEmpty(url.UserInfo))
        {
            Add("url", "Put credentials in the auth settings or a header, not in the URL.");
        }
        else if (url.OriginalString.Length > ExecutionRules.UrlMaxLength)
        {
            Add("url", $"URL must be at most {ExecutionRules.UrlMaxLength} characters.");
        }

        if (request.Headers.Count > ExecutionRules.MaxHeaders)
        {
            Add("headers", $"At most {ExecutionRules.MaxHeaders} headers are allowed.");
        }

        foreach (var header in request.Headers)
        {
            if (!IsToken(header.Name))
            {
                Add("headers", $"'{header.Name}' is not a valid header name.");
            }
            else if (ExecutionRules.ForbiddenHeaders.Contains(header.Name))
            {
                Add("headers", $"The {header.Name} header is set automatically and can't be overridden.");
            }

            if (HasLineBreak(header.Value))
            {
                Add("headers", $"The value of '{header.Name}' can't contain line breaks.");
            }
        }

        if (request.Auth is { } auth)
        {
            if (string.IsNullOrEmpty(auth.Value) || HasLineBreak(auth.Value))
            {
                Add("auth.value", "The auth value is required and can't contain line breaks.");
            }

            if (auth.Kind != AuthKind.Bearer && (auth.Name is null || !IsToken(auth.Name)))
            {
                Add("auth.name", "An API key needs a valid header or query parameter name.");
            }
            else if (auth.Kind == AuthKind.ApiKeyHeader && ExecutionRules.ForbiddenHeaders.Contains(auth.Name!))
            {
                Add("auth.name", $"The {auth.Name} header can't be used for an API key.");
            }
        }

        var contentType = ResolveContentType(request);
        if (contentType is not null && !MediaTypeHeaderValue.TryParse(contentType, out _))
        {
            Add("contentType", $"'{contentType}' is not a valid content type.");
        }

        if (request.Body is not null && Encoding.UTF8.GetByteCount(request.Body) > _options.MaxRequestBodyBytes)
        {
            Add("body", $"The body must be at most {_options.MaxRequestBodyBytes / 1024} KB.");
        }

        return errors;
    }

    /// <summary>
    /// Sends the request and reads the response. Never throws for failures of the target (blocked address,
    /// DNS, refused connection, TLS, timeout); those come back in <see cref="ExecutionResult.Error"/>.
    /// Throws <see cref="OperationCanceledException"/> only when <paramref name="cancellationToken"/> fires.
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(OutboundRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Failed(ExecutionErrorCodes.InvalidRequest, errors.Values.First()[0], 0);
        }

        using var message = BuildMessage(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var host = request.Url.Host;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var (bytes, truncated) = await ReadCappedAsync(response.Content, timeout.Token);
            stopwatch.Stop();

            var mediaType = response.Content.Headers.ContentType;
            var isBinary = !IsText(mediaType?.MediaType, bytes);
            var body = isBinary ? "" : Decode(bytes, mediaType?.CharSet);

            // Host only: the path and query can hold user data or keys.
            LogExecuted(request.Method, host, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            return new ExecutionResult(
                (int)response.StatusCode,
                response.ReasonPhrase,
                FlattenHeaders(response),
                body,
                truncated,
                isBinary,
                bytes.Length,
                stopwatch.ElapsedMilliseconds,
                Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return LoggedFailure(ExecutionErrorCodes.Timeout,
                $"No complete response within {_options.TimeoutSeconds} seconds.", request.Method, host, stopwatch);
        }
        catch (HttpRequestException ex)
        {
            var (code, text) = Describe(ex, host);
            return LoggedFailure(code, text, request.Method, host, stopwatch);
        }
    }

    private static HttpRequestMessage BuildMessage(OutboundRequest request)
    {
        var url = request.Url;
        if (request.Auth is { Kind: AuthKind.ApiKeyQuery } queryAuth)
        {
            var separator = string.IsNullOrEmpty(url.Query) ? "?" : "&";
            url = new Uri(url.AbsoluteUri + separator
                + Uri.EscapeDataString(queryAuth.Name!) + "=" + Uri.EscapeDataString(queryAuth.Value));
        }

        var message = new HttpRequestMessage(new HttpMethod(request.Method), url);

        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8);
            message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ResolveContentType(request) ?? "text/plain; charset=utf-8");
        }

        foreach (var header in request.Headers)
        {
            if (header.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue; // Applied to the content above.
            }

            // Content headers (Content-Language and so on) belong on the content; the rest on the message.
            if (!message.Headers.TryAddWithoutValidation(header.Name, header.Value))
            {
                message.Content?.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
        }

        switch (request.Auth)
        {
            case { Kind: AuthKind.Bearer } bearer:
                message.Headers.Remove("Authorization");
                message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer.Value);
                break;
            case { Kind: AuthKind.ApiKeyHeader } apiKey:
                message.Headers.Remove(apiKey.Name!);
                message.Headers.TryAddWithoutValidation(apiKey.Name!, apiKey.Value);
                break;
        }

        if (!message.Headers.Contains("User-Agent"))
        {
            message.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        }

        return message;
    }

    private static string? ResolveContentType(OutboundRequest request) =>
        request.ContentType
        ?? request.Headers.FirstOrDefault(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>Reads at most MaxResponseBytes, then stops, so a huge or endless response can't exhaust memory.</summary>
    private async Task<(byte[] Bytes, bool Truncated)> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var limit = _options.MaxResponseBytes;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                var room = limit - (int)buffer.Length;
                if (read > room)
                {
                    buffer.Write(chunk, 0, room);
                    return (buffer.ToArray(), true);
                }

                buffer.Write(chunk, 0, read);
            }

            return (buffer.ToArray(), false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private static List<HeaderPair> FlattenHeaders(HttpResponseMessage response) =>
    [
        .. response.Headers.Concat(response.Content.Headers)
            .SelectMany(header => header.Value.Select(value => new HeaderPair(header.Key, value))),
    ];

    private static bool IsText(string? mediaType, byte[] bytes)
    {
        if (mediaType is null)
        {
            // No Content-Type: treat it as text unless it contains NUL bytes, which text practically never does.
            return Array.IndexOf(bytes, (byte)0) < 0;
        }

        return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/graphql", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // Unknown charset: UTF-8 is the best guess.
            }
        }

        return encoding.GetString(bytes);
    }

    private static (string Code, string Message) Describe(HttpRequestException ex, string host)
    {
        if (ex.InnerException is BlockedAddressException)
        {
            return (ExecutionErrorCodes.BlockedAddress,
                $"'{host}' resolves to a private, loopback or other internal address, and requests there are blocked. "
                + "Requests run on the API Architect server, so they can only reach APIs on the public internet.");
        }

        if (ex.InnerException is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain }
            || ex.HttpRequestError == HttpRequestError.NameResolutionError)
        {
            return (ExecutionErrorCodes.DnsFailure, $"Couldn't resolve '{host}'. Check the host name.");
        }

        return ex.HttpRequestError switch
        {
            HttpRequestError.SecureConnectionError =>
                (ExecutionErrorCodes.TlsFailure, $"The TLS connection to '{host}' failed. Its certificate may be invalid or untrusted."),
            HttpRequestError.ConnectionError =>
                (ExecutionErrorCodes.ConnectionFailed, $"Couldn't connect to '{host}'. The server refused or didn't answer."),
            HttpRequestError.InvalidResponse or HttpRequestError.ResponseEnded =>
                (ExecutionErrorCodes.InvalidResponse, $"'{host}' sent an invalid or incomplete HTTP response."),
            _ => (ExecutionErrorCodes.RequestFailed, $"The request to '{host}' failed."),
        };
    }

    private ExecutionResult LoggedFailure(string code, string message, string method, string host, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        LogFailed(method, host, code, stopwatch.ElapsedMilliseconds);
        return Failed(code, message, stopwatch.ElapsedMilliseconds);
    }

    private static ExecutionResult Failed(string code, string message, long durationMs) =>
        new(null, null, [], "", false, false, 0, durationMs, new ExecutionError(code, message));

    private static bool IsToken(string value) => TokenRegex().IsMatch(value);

    private static bool HasLineBreak(string value) => value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0;

    /// <summary>An RFC 9110 token: what a header name may contain.</summary>
    [GeneratedRegex(@"^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$")]
    private static partial Regex TokenRegex();

    [LoggerMessage(LogLevel.Information, "Executed {Method} request to {Host}: {StatusCode} in {DurationMs} ms")]
    private partial void LogExecuted(string method, string host, int statusCode, long durationMs);

    [LoggerMessage(LogLevel.Information, "{Method} request to {Host} failed: {ErrorCode} after {DurationMs} ms")]
    private partial void LogFailed(string method, string host, string errorCode, long durationMs);

    public void Dispose() => _client.Dispose();
}
