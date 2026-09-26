using YoPay.Application.Webhooks;

namespace YoPay.UnitTests;

public class WebhookRetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_retry_is_a_minute_away()
    {
        // Most failures are a deploy or a restart, and a minute covers them.
        var verdict = WebhookRetry.After(attempt: 1, responseCode: 503, Now);

        Assert.Equal(Now.AddMinutes(1), verdict.NextAttemptAt);
    }

    [Fact]
    public void The_backoff_widens_rather_than_hammering()
    {
        var delays = Enumerable.Range(1, WebhookRetry.MaxAttempts - 1)
            .Select(a => WebhookRetry.After(a, 503, Now).NextAttemptAt!.Value - Now)
            .ToList();

        Assert.Equal(delays.OrderBy(d => d), delays);
        Assert.True(delays[^1] >= TimeSpan.FromHours(2));
    }

    [Fact]
    public void After_the_last_attempt_it_stops()
    {
        // Retrying a dead endpoint forever is a queue that never drains, and from the
        // merchant's side it is traffic their host reads as an attack.
        var verdict = WebhookRetry.After(WebhookRetry.MaxAttempts, 503, Now);

        Assert.True(verdict.GivesUp);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(408)]
    public void A_server_that_is_struggling_gets_another_chance(int code)
    {
        Assert.False(WebhookRetry.After(1, code, Now).GivesUp);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(400)]
    public void A_server_that_has_answered_clearly_is_not_pestered(int code)
    {
        // These do not change in five minutes. Retrying them is noise for the merchant
        // and work for us, and it buries the deliveries that would have succeeded.
        Assert.True(WebhookRetry.After(1, code, Now).GivesUp);
    }

    [Fact]
    public void Nothing_answering_at_all_is_always_worth_another_try()
    {
        // A refused connection or a timeout says nothing about whether the endpoint is
        // real - only that it was not reachable in that second.
        Assert.False(WebhookRetry.After(1, responseCode: null, Now).GivesUp);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    public void Any_2xx_counts_as_delivered(int code)
    {
        // Merchants return all of these. Insisting on 200 would dead-letter deliveries
        // that arrived perfectly well.
        Assert.True(WebhookRetry.IsSuccess(code));
    }

    [Theory]
    [InlineData(302)]
    [InlineData(199)]
    [InlineData(404)]
    public void Anything_else_does_not(int code)
    {
        Assert.False(WebhookRetry.IsSuccess(code));
    }
}
