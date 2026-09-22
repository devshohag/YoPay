using YoPay.Application.Ingestion;

namespace YoPay.UnitTests;

public class DedupeHashTests
{
    private static readonly Guid DeviceId = Guid.CreateVersion7();
    private static readonly DateTimeOffset At = new(2026, 8, 13, 16, 2, 0, TimeSpan.FromHours(6));
    private const string Body = "You have received Tk 200.00 from 01910126335. TrxID DHD6EYO3HO";

    [Fact]
    public void The_same_message_hashes_the_same_way_every_time()
    {
        Assert.Equal(
            DedupeHash.Compute(DeviceId, "bKash", Body, At),
            DedupeHash.Compute(DeviceId, "bKash", Body, At));
    }

    [Fact]
    public void Whitespace_differences_do_not_produce_a_second_row()
    {
        // The notification listener and the SMS receiver hand over the same message with
        // different spacing. Without this they would both be stored.
        Assert.Equal(
            DedupeHash.Compute(DeviceId, "bKash", Body, At),
            DedupeHash.Compute(DeviceId, "bKash", $"  {Body.Replace(" ", "  ")}  ", At));
    }

    [Fact]
    public void Two_identical_payments_minutes_apart_are_two_events()
    {
        // A wallet really can receive the same amount from the same sender twice. The
        // timestamp is in the hash so those stay two payments, not one message repeated.
        Assert.NotEqual(
            DedupeHash.Compute(DeviceId, "bKash", Body, At),
            DedupeHash.Compute(DeviceId, "bKash", Body, At.AddMinutes(3)));
    }

    [Fact]
    public void The_same_message_seen_by_two_handsets_is_two_events()
    {
        // Both phones watch the same wallet. Deduplication across devices would hide a
        // failover gap rather than expose it.
        Assert.NotEqual(
            DedupeHash.Compute(DeviceId, "bKash", Body, At),
            DedupeHash.Compute(Guid.CreateVersion7(), "bKash", Body, At));
    }

    [Fact]
    public void Sender_case_does_not_matter()
    {
        Assert.Equal(
            DedupeHash.Compute(DeviceId, "bKash", Body, At),
            DedupeHash.Compute(DeviceId, "BKASH", Body, At));
    }
}
