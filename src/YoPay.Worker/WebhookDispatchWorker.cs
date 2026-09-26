using YoPay.Application.Webhooks;

namespace YoPay.Worker;

/// <summary>
/// Carries settled payments out to the merchants who asked to hear about them.
///
/// A second loop rather than more work inside the payment pipeline. A merchant's server
/// can take thirty seconds to answer, or never answer; wiring that into the matcher would
/// mean a slow shop in one district holding up the settlement of everybody else's
/// payments. The outbox is the seam that lets the two run at their own speeds.
///
/// Polls a little slower than the payment loop. Nobody is watching a webhook land the way
/// they watch an invoice turn green, and every empty poll is a query.
/// </summary>
public sealed class WebhookDispatchWorker(
    IServiceScopeFactory scopes,
    ILogger<WebhookDispatchWorker> log) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Webhook dispatcher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = 0;

            try
            {
                using var scope = scopes.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<WebhookPipeline>();

                var run = await pipeline.RunOnceAsync(ct: stoppingToken).ConfigureAwait(false);
                worked = run.Worked;

                Report(run);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Webhook dispatch iteration failed");
            }

            if (worked == 0)
            {
                await Task.Delay(IdleDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private void Report(WebhookRunResult run)
    {
        if (run.NotificationsWithNowhereToGo > 0)
        {
            // Worth saying out loud: a merchant is taking payments and nothing on their
            // side is being told about them. Usually it means setup was never finished.
            log.LogWarning(
                "{Count} notifications had no endpoint to go to",
                run.NotificationsWithNowhereToGo);
        }

        foreach (var delivery in run.Deliveries)
        {
            if (delivery.Succeeded)
            {
                log.LogInformation(
                    "Delivered {Event} to {Url} ({Code})",
                    delivery.EventType, delivery.Url, delivery.ResponseCode);
            }
            else if (delivery.DeadLettered)
            {
                // The end of the line for this one. It stays on the dashboard to be
                // replayed by hand, and nobody finds out about it unless this is loud.
                log.LogError(
                    "Gave up delivering {Event} to {Url}: {Error}",
                    delivery.EventType, delivery.Url, delivery.Error);
            }
            else
            {
                log.LogWarning(
                    "Delivery of {Event} to {Url} failed, retrying at {RetryAt}: {Error}",
                    delivery.EventType, delivery.Url, delivery.RetryAt, delivery.Error);
            }
        }
    }
}
