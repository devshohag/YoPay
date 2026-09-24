using YoPay.Application.Matching;
using YoPay.Domain.Enums;

namespace YoPay.Worker;

/// <summary>
/// Drains the ingest queue on a short poll.
///
/// Polling rather than a message broker because the queue is a table the ingest endpoint
/// already writes to, and one moving part that cannot desynchronise beats two that can.
/// When volume makes the poll wasteful, a NOTIFY on insert turns the delay into a wake-up
/// without changing anything else here.
/// </summary>
public sealed class PaymentPipelineWorker(
    IServiceScopeFactory scopes,
    ILogger<PaymentPipelineWorker> log) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Payment pipeline started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;

            try
            {
                using var scope = scopes.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<PaymentPipeline>();

                var run = await pipeline.RunOnceAsync(ct: stoppingToken).ConfigureAwait(false);
                processed = run.Processed;

                Report(run);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad message must never stop the pipeline. The next loop picks up
                // where this one failed and the row stays visible for a person to look at.
                log.LogError(ex, "Payment pipeline iteration failed");
            }

            if (processed == 0)
            {
                await Task.Delay(IdleDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private void Report(PipelineRunResult run)
    {
        foreach (var item in run.Items)
        {
            if (item.Error is not null)
            {
                // A message that threw. Loud, with the row id, because this is the line
                // that explains a payment the system appeared to swallow.
                log.LogError(
                    "Raw event {RawEventId} failed in the pipeline: {Error}",
                    item.RawEventId, item.Error);
            }
            else if (item.Match is MatchOutcome.Matched or MatchOutcome.Partial)
            {
                log.LogInformation(
                    "Settled invoice {InvoiceId} from {TrxId} for {Amount} by {Strategy}",
                    item.InvoiceId, item.TrxId, item.Amount, item.Strategy);
            }
            else if (item.State == RawEventState.Unparseable)
            {
                // The loudest line in the file, on purpose: this is how a wording change
                // at the operator announces itself, and every minute it goes unnoticed is
                // a minute of payments not being recognised.
                log.LogWarning(
                    "Unparseable message on raw event {RawEventId}: {Reason}",
                    item.RawEventId, item.Reason);
            }
            else if (item.Match is MatchOutcome.NeedsReview or MatchOutcome.Unmatched)
            {
                log.LogWarning(
                    "Payment {TrxId} for {Amount} was not settled: {Outcome} {Reason}",
                    item.TrxId, item.Amount, item.Match, item.Reason);
            }
        }
    }
}
