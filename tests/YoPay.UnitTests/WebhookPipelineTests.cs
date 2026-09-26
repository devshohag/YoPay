using YoPay.Application.Abstractions;
using YoPay.Application.Webhooks;
using YoPay.Domain.Enums;

namespace YoPay.UnitTests;

public class WebhookPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 17, 0, 0, TimeSpan.Zero);

    private static PendingNotification Notification() => new()
    {
        OutboxId = Guid.CreateVersion7(),
        MerchantId = Guid.CreateVersion7(),
        InvoiceId = Guid.CreateVersion7(),
        Type = "payment.paid",
        PayloadJson = """{"event":"payment.paid"}""",
    };

    private static DueDelivery Delivery(int attempt = 0) => new()
    {
        DeliveryId = Guid.CreateVersion7(),
        EndpointId = Guid.CreateVersion7(),
        Url = "https://shop.example.com/yopay",
        Secret = "secret",
        PayloadJson = """{"event":"payment.paid"}""",
        EventType = "payment.paid",
        Attempt = attempt,
    };

    [Fact]
    public async Task A_notification_becomes_one_delivery_per_endpoint()
    {
        var store = new WebhookStoreSpy
        {
            Notifications = [Notification()],
            Targets = [Target("https://one.example.com"), Target("https://two.example.com")],
        };

        var run = await Pipeline(store, new SenderSpy()).RunOnceAsync();

        Assert.Equal(1, run.NotificationsFannedOut);
        Assert.Equal(2, store.FannedOutTo.Count);
    }

    [Fact]
    public async Task A_merchant_with_no_endpoint_is_a_setup_problem_not_a_retry()
    {
        // Waiting will not produce an endpoint. The notification is closed, with a
        // sentence, so somebody can see that payments are settling into silence.
        var store = new WebhookStoreSpy { Notifications = [Notification()], Targets = [] };

        var run = await Pipeline(store, new SenderSpy()).RunOnceAsync();

        Assert.Equal(1, run.NotificationsWithNowhereToGo);
        Assert.Single(store.Closed);
        Assert.Contains("No active, validated endpoint", store.Closed[0].Reason);
    }

    [Fact]
    public async Task A_delivered_notification_is_recorded_and_not_retried()
    {
        var store = new WebhookStoreSpy { Due = [Delivery()] };

        var run = await Pipeline(store, new SenderSpy { Code = 200 }).RunOnceAsync();

        Assert.Equal(1, run.Sent);
        Assert.Single(store.Succeeded);
        Assert.Empty(store.Failed);
    }

    [Fact]
    public async Task A_server_that_is_down_is_scheduled_rather_than_abandoned()
    {
        var store = new WebhookStoreSpy { Due = [Delivery()] };

        var run = await Pipeline(store, new SenderSpy { Code = 503 }).RunOnceAsync();

        Assert.Equal(1, run.Failed);
        Assert.Equal(0, run.DeadLettered);
        Assert.Equal(Now.AddMinutes(1), store.Failed[0].NextAttemptAt);
    }

    [Fact]
    public async Task A_server_that_says_the_endpoint_is_gone_is_not_pestered()
    {
        var store = new WebhookStoreSpy { Due = [Delivery()] };

        var run = await Pipeline(store, new SenderSpy { Code = 404 }).RunOnceAsync();

        Assert.Equal(1, run.DeadLettered);
        Assert.Null(store.Failed[0].NextAttemptAt);
    }

    [Fact]
    public async Task The_last_attempt_dead_letters_instead_of_disappearing()
    {
        var store = new WebhookStoreSpy
        {
            Due = [Delivery(attempt: WebhookRetry.MaxAttempts - 1)],
        };

        var run = await Pipeline(store, new SenderSpy { Code = 503 }).RunOnceAsync();

        Assert.Equal(1, run.DeadLettered);
        Assert.Null(store.Failed[0].NextAttemptAt);
    }

    [Fact]
    public async Task One_broken_endpoint_does_not_stop_the_others()
    {
        // The lesson the payment pipeline learned the hard way, applied here before it
        // had a chance to cost anything.
        var good = Delivery();
        var bad = Delivery();
        var store = new WebhookStoreSpy { Due = [bad, good] };

        var sender = new SenderSpy { Code = 200, ThrowFor = bad.DeliveryId };

        var run = await Pipeline(store, sender).RunOnceAsync();

        Assert.Equal(2, run.Deliveries.Count);
        Assert.Equal(1, run.Sent);
        Assert.Contains("the socket melted", store.Failed[0].Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_with_no_answer_at_all_still_says_something_useful()
    {
        var store = new WebhookStoreSpy { Due = [Delivery()] };

        var run = await Pipeline(store, new SenderSpy { Code = null }).RunOnceAsync();

        Assert.False(string.IsNullOrWhiteSpace(run.Deliveries[0].Error));
    }

    private static WebhookPipeline Pipeline(IWebhookStore store, IWebhookSender sender) =>
        new(store, sender, new FixedClock(Now));

    private static EndpointTarget Target(string url) => new()
    {
        EndpointId = Guid.CreateVersion7(),
        Url = url,
        Secret = "secret",
    };

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed record FailureRecord(
        Guid DeliveryId, int Attempt, int? ResponseCode, string Error, DateTimeOffset? NextAttemptAt);

    private sealed class WebhookStoreSpy : IWebhookStore
    {
        public List<PendingNotification> Notifications = [];
        public List<EndpointTarget> Targets = [];
        public List<DueDelivery> Due = [];

        public List<EndpointTarget> FannedOutTo = [];
        public List<(Guid OutboxId, string Reason)> Closed = [];
        public List<Guid> Succeeded = [];
        public List<FailureRecord> Failed = [];

        public Task<IReadOnlyList<PendingNotification>> ClaimPendingNotificationsAsync(
            int batchSize, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingNotification>>(Notifications);

        public Task<IReadOnlyList<EndpointTarget>> FindTargetsAsync(
            Guid merchantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EndpointTarget>>(Targets);

        public Task FanOutAsync(
            PendingNotification notification,
            IReadOnlyList<EndpointTarget> targets,
            CancellationToken ct = default)
        {
            FannedOutTo.AddRange(targets);
            return Task.CompletedTask;
        }

        public Task CloseUndeliverableAsync(Guid outboxId, string reason, CancellationToken ct = default)
        {
            Closed.Add((outboxId, reason));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DueDelivery>> ClaimDueDeliveriesAsync(
            int batchSize, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DueDelivery>>(Due);

        public Task RecordSuccessAsync(Guid deliveryId, int responseCode, CancellationToken ct = default)
        {
            Succeeded.Add(deliveryId);
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(
            Guid deliveryId, int attempt, int? responseCode, string failureReason,
            DateTimeOffset? nextAttemptAt, CancellationToken ct = default)
        {
            Failed.Add(new FailureRecord(deliveryId, attempt, responseCode, failureReason, nextAttemptAt));
            return Task.CompletedTask;
        }

        public Task SetEndpointStateAsync(
            Guid endpointId, WebhookEndpointState state, string? failureReason,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class SenderSpy : IWebhookSender
    {
        public int? Code { get; set; }
        public Guid? ThrowFor { get; set; }

        public Task<SendResult> SendAsync(DueDelivery delivery, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(delivery);

            if (ThrowFor == delivery.DeliveryId)
            {
                throw new InvalidOperationException("the socket melted");
            }

            return Task.FromResult(new SendResult
            {
                ResponseCode = Code,
                Error = Code is null ? "The endpoint did not answer." : null,
            });
        }
    }
}
