using YoPay.Domain.Enums;

namespace YoPay.Application.Matching;

/// <summary>One message waiting to be read.</summary>
public sealed record PendingRawEvent
{
    public required Guid RawEventId { get; init; }
    public required Guid MerchantId { get; init; }
    public required Guid WalletId { get; init; }
    public required string SenderId { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset DeviceReceivedAt { get; init; }
}

public interface IPipelineStore
{
    /// <summary>
    /// Takes the next batch of unread messages. Uses SKIP LOCKED so a second worker picks
    /// up different rows rather than waiting behind the first.
    /// </summary>
    Task<IReadOnlyList<PendingRawEvent>> ClaimUnparsedAsync(
        int batchSize, CancellationToken ct = default);

    /// <summary>
    /// Moves a message to its final state, with a sentence saying why when that state is
    /// a failure. The reason is stored on the row rather than only logged: the person who
    /// has to act on a stuck message is looking at the queue, not at a log file.
    /// </summary>
    Task MarkStateAsync(
        Guid rawEventId,
        RawEventState state,
        string? failureReason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Stores the reading. Returns null when a transaction with this id already exists,
    /// which happens when the same payment reaches us twice - the provider sends a hold
    /// and a completion carrying one id, and a handset uploads its backlog more than once.
    /// </summary>
    Task<Guid?> SaveParsedAsync(
        PendingRawEvent source,
        PaymentMethod method,
        decimal amount,
        string trxId,
        string? senderMsisdn,
        decimal? balanceAfter,
        DateTimeOffset occurredAt,
        decimal confidence,
        CancellationToken ct = default);
}
