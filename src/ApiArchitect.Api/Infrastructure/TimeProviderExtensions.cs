namespace ApiArchitect.Api.Infrastructure;

public static class TimeProviderExtensions
{
    /// <summary>
    /// The current UTC time cut to whole microseconds, the precision PostgreSQL stores.
    /// Use this for values you save, so the response you return right after saving matches
    /// what a later read returns.
    /// </summary>
    public static DateTimeOffset GetUtcNowForStorage(this TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
