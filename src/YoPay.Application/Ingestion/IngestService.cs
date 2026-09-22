using YoPay.Application.Abstractions;
using YoPay.Application.Devices;
using YoPay.Contracts.Ingest;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.Application.Ingestion;

/// <summary>
/// Takes a batch of observations from one handset and stores what is new.
///
/// Two rules, both of them refusals:
///
/// 1. A sender id outside the whitelist is dropped here, before anything is written.
///    Anyone can send a text that looks exactly like a bKash confirmation; the sender is
///    all that separates a real one from a forgery on the handset.
///
/// 2. An event dated far in the future is dropped. A phone with a badly wrong clock would
///    otherwise write events that stay eligible for matching long after every real
///    payment window has closed.
/// </summary>
public sealed class IngestService(IRawEventStore events, IClock clock)
{
    /// <summary>Generous enough for an ordinary unsynced clock, short enough that a
    /// wrong one cannot park events in the future.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(10);

    /// <summary>Older than this and no open window could still want it.</summary>
    public static readonly TimeSpan MaxBacklogAge = TimeSpan.FromDays(7);

    private static readonly HashSet<string> AllowedSenders =
        new(StringComparer.OrdinalIgnoreCase) { "bKash", "16247" };

    public async Task<IngestOutcome> IngestAsync(
        DeviceIdentity device,
        IReadOnlyList<DeviceEvent> batch,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(batch);

        var now = clock.UtcNow;
        int accepted = 0, duplicates = 0, rejected = 0;

        foreach (var item in batch)
        {
            if (!IsAcceptable(item, now))
            {
                rejected++;
                continue;
            }

            // Recomputed here, never taken from the device: a client-supplied hash could
            // make two different messages collide, or one message look like ten.
            var hash = DedupeHash.Compute(
                device.DeviceId, item.SenderId, item.Body, item.ReceivedAt);

            var rawEvent = new RawEvent
            {
                MerchantId = device.MerchantId,
                DeviceId = device.DeviceId,
                Source = item.Source,
                SenderId = item.SenderId.Trim(),
                Body = item.Body.Trim(),
                DeviceReceivedAt = item.ReceivedAt,
                ServerReceivedAt = now,
                DedupeHash = hash,
                State = RawEventState.Received,
            };

            if (await events.AddIfNewAsync(rawEvent, ct).ConfigureAwait(false))
            {
                accepted++;
            }
            else
            {
                duplicates++;
            }
        }

        return new IngestOutcome
        {
            Accepted = accepted,
            Duplicates = duplicates,
            Rejected = rejected,
        };
    }

    public static bool IsAcceptable(DeviceEvent item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(item.Body) || item.Body.Length > 2000)
        {
            return false;
        }

        if (!AllowedSenders.Contains(item.SenderId?.Trim() ?? ""))
        {
            return false;
        }

        if (item.ReceivedAt > now + MaxClockSkew)
        {
            return false;
        }

        return item.ReceivedAt >= now - MaxBacklogAge;
    }
}
