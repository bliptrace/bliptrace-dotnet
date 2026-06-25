namespace G8Scope;

/// <summary>
/// Decides whether a failed ingest attempt should be retried, and how long to wait.
///
/// The SDK owns "the backend might be briefly down or shedding (429)". Strategy:
/// exponential backoff (base × 2^attempt), ±25% jitter to avoid a synchronised
/// thundering herd, and a hard cap on both attempts and delay so capturing can never
/// block the monitored app for long. Retries are only safe because the envelope's
/// event_id is reused across attempts and the ingest API dedupes on it (spec §10).
///
/// Pure and immutable: no I/O, no sleeping — that lives in the transport.
/// </summary>
public sealed class RetryPolicy
{
    private static readonly Random Rng = new();

    public RetryPolicy(int maxRetries = 2, int baseMs = 100, int capMs = 2000)
    {
        MaxRetries = maxRetries;
        BaseMs = baseMs;
        CapMs = capMs;
    }

    public int MaxRetries { get; }

    public int BaseMs { get; }

    public int CapMs { get; }

    /// <summary>Build from the SDK config (framework adapters pass their own values).</summary>
    public static RetryPolicy FromConfig(Config config)
    {
        return new RetryPolicy(config.MaxRetries, config.RetryBaseMs, config.RetryCapMs);
    }

    /// <summary>
    /// Should we retry after seeing <paramref name="status"/>, having already done
    /// <paramref name="attempt"/> retries?
    ///
    /// Retryable: transport errors / timeouts (status 0), 408, 429, and any 5xx.
    /// Never retry a 2xx (success) or other 4xx (the request itself is bad).
    /// </summary>
    public bool ShouldRetry(int status, int attempt)
    {
        if (attempt >= MaxRetries)
        {
            return false;
        }

        return IsRetryable(status);
    }

    public bool IsRetryable(int status)
    {
        return status == 0 || status == 408 || status == 429 || status >= 500;
    }

    /// <summary>
    /// Delay before the next attempt, in milliseconds.
    ///
    /// A server-sent Retry-After (seconds) wins, capped to <see cref="CapMs"/> so a
    /// hostile or misconfigured header can't stall the app. Otherwise: exponential
    /// backoff with ±25% jitter, capped at <see cref="CapMs"/>. <paramref name="jitter"/>
    /// returns a double in [0,1) and is injectable for deterministic tests; it defaults
    /// to a uniform random draw.
    /// </summary>
    /// <param name="retryAfterSec">Server Retry-After in seconds; 0 (or negative) = none.</param>
    public int DelayMs(int attempt, int retryAfterSec = 0, Func<double>? jitter = null)
    {
        if (retryAfterSec > 0)
        {
            return Math.Min(CapMs, retryAfterSec * 1000);
        }

        var exp = Math.Min(CapMs, BaseMs * (1 << Math.Max(0, attempt)));
        var rand = jitter?.Invoke() ?? Rng.NextDouble();
        var delta = (int)Math.Round(exp * 0.25 * (2 * rand - 1)); // ±25%

        return Math.Max(0, Math.Min(CapMs, exp + delta));
    }
}
