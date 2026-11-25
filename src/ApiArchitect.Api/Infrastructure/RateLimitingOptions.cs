namespace ApiArchitect.Api.Infrastructure;

/// <summary>Bound from the "RateLimiting" configuration section.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Register and login attempts allowed per client IP per minute.</summary>
    public int AuthPermitsPerMinute { get; init; } = 10;
}
