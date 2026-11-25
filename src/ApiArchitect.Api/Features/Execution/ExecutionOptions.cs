namespace ApiArchitect.Api.Features.Execution;

/// <summary>Bound from the "Execution" configuration section. Limits for requests users send through the server.</summary>
public sealed class ExecutionOptions
{
    public const string SectionName = "Execution";

    /// <summary>Total time for one request: connect, send, and read the whole response.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Time allowed to open the TCP (and TLS) connection.</summary>
    public int ConnectTimeoutSeconds { get; init; } = 10;

    /// <summary>Response bytes read from the target. Anything past this is dropped and the body is marked truncated.</summary>
    public int MaxResponseBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>Largest request body a user may send, in UTF-8 bytes.</summary>
    public int MaxRequestBodyBytes { get; init; } = 1024 * 1024;

    /// <summary>Request and response body bytes kept in history. The live response is not affected.</summary>
    public int MaxStoredBodyBytes { get; init; } = 64 * 1024;

    /// <summary>Executions kept per project; older ones are deleted when a new one is recorded.</summary>
    public int HistoryLimitPerProject { get; init; } = 100;

    /// <summary>Executions allowed per user per minute.</summary>
    public int PermitsPerMinute { get; init; } = 60;

    /// <summary>
    /// CIDR ranges exempt from the internal-address block, such as <c>10.1.0.0/16</c>. Empty by default.
    /// Only set this for a self-hosted install that must reach a private network, and never include the range
    /// this app or its database runs in. The integration tests use it to reach a loopback test server.
    /// </summary>
    public string[] AllowedNetworks { get; init; } = [];
}
