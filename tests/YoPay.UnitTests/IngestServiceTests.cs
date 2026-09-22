using YoPay.Application.Ingestion;
using YoPay.Contracts.Ingest;
using YoPay.Domain.Enums;

namespace YoPay.UnitTests;

public class IngestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private static DeviceEvent Event(
        string sender = "bKash", string body = "You have received Tk 200.00", DateTimeOffset? at = null) =>
        new()
        {
            Source = EventSource.Notification,
            SenderId = sender,
            Body = body,
            ReceivedAt = at ?? Now.AddMinutes(-1),
            DedupeHash = "ignored",
        };

    [Fact]
    public void An_ordinary_message_is_accepted()
    {
        Assert.True(IngestService.IsAcceptable(Event(), Now));
    }

    [Theory]
    [InlineData("BKASH-PROMO")]
    [InlineData("16248")]
    [InlineData("")]
    public void A_message_from_anyone_but_the_operator_is_dropped(string sender)
    {
        // Anyone can send a text that looks exactly like a confirmation. The sender id is
        // all that separates a real one from a forgery on the handset.
        Assert.False(IngestService.IsAcceptable(Event(sender: sender), Now));
    }

    [Fact]
    public void A_message_dated_in_the_future_is_dropped()
    {
        // A handset with a badly wrong clock would otherwise write events that stay
        // eligible for matching long after every real window has closed.
        Assert.False(IngestService.IsAcceptable(
            Event(at: Now.AddHours(1)), Now));
    }

    [Fact]
    public void Modest_clock_drift_is_tolerated()
    {
        Assert.True(IngestService.IsAcceptable(Event(at: Now.AddMinutes(5)), Now));
    }

    [Fact]
    public void A_week_old_backlog_is_still_accepted_but_a_year_old_one_is_not()
    {
        Assert.True(IngestService.IsAcceptable(Event(at: Now.AddDays(-6)), Now));
        Assert.False(IngestService.IsAcceptable(Event(at: Now.AddDays(-365)), Now));
    }

    [Fact]
    public void An_empty_or_absurdly_long_body_is_dropped()
    {
        Assert.False(IngestService.IsAcceptable(Event(body: "   "), Now));
        Assert.False(IngestService.IsAcceptable(Event(body: new string('x', 2001)), Now));
    }
}
