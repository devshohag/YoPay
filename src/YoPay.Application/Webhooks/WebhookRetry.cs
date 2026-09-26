namespace YoPay.Application.Webhooks;

/// <summary>What to do after one failed attempt.</summary>
public sealed record RetryVerdict
{
    /// <summary>Null when there is nothing left to try.</summary>
    public DateTimeOffset? NextAttemptAt { get; init; }

    public bool GivesUp => NextAttemptAt is null;
}

/// <summary>
/// When to try a failed delivery again, and when to stop.
///
/// The schedule is 1 minute, 5, 30, 2 hours, 12 hours, then dead letter: roughly fifteen
/// hours of patience spread over five attempts. That shape is deliberate. Most failures
/// are a shop restarting or a deploy, which the first two attempts cover; the long tail
/// exists for an expired certificate or a hosting bill nobody paid, which takes someone
/// waking up and fixing it.
///
/// Retrying forever is worse than stopping. A dead endpoint retried indefinitely turns
/// into a queue that never drains and, from the other side, into traffic the merchant's
/// host reads as an attack. A dead letter is visible on the dashboard and can be replayed
/// by hand once the endpoint is back, which is the honest version of the same promise.
///
/// Not every failure deserves a retry, and that is the second rule here. A 404 or a 410
/// means the endpoint is not there and will not be there in five minutes; a 401 or a 403
/// means the merchant's own code is rejecting us. Retrying those five times is noise for
/// them and work for us. A 429 or a 5xx is the opposite: the endpoint exists and is
/// asking for time.
/// </summary>
public static class WebhookRetry
{
    public static readonly IReadOnlyList<TimeSpan> Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12),
    ];

    public static int MaxAttempts => Backoff.Count;

    /// <summary>
    /// <paramref name="attempt"/> is the number of attempts made so far, including the
    /// one that just failed.
    /// </summary>
    public static RetryVerdict After(int attempt, int? responseCode, DateTimeOffset now)
    {
        if (responseCode is { } code && !IsWorthRetrying(code))
        {
            return new RetryVerdict { NextAttemptAt = null };
        }

        if (attempt >= MaxAttempts || attempt < 1)
        {
            return new RetryVerdict { NextAttemptAt = null };
        }

        return new RetryVerdict { NextAttemptAt = now + Backoff[attempt - 1] };
    }

    /// <summary>
    /// A transport failure - connection refused, timeout, TLS error - has no status code
    /// and is always worth retrying: it says nothing about whether the endpoint is real.
    /// </summary>
    public static bool IsWorthRetrying(int responseCode) =>
        responseCode is 408 or 425 or 429 || responseCode >= 500;

    public static bool IsSuccess(int responseCode) =>
        responseCode is >= 200 and < 300;
}
