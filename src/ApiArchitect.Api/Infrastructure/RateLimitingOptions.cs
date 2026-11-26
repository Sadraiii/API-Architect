namespace ApiArchitect.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Register and login attempts allowed per client IP per minute.</summary>
    public int AuthPermitsPerMinute { get; init; } = 10;
}
