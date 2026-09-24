using YoPay.Application.Parsing;

namespace YoPay.UnitTests;

/// <summary>
/// Replays every message in the golden corpus.
///
/// The corpus is real traffic from a live account, not messages written to suit the
/// patterns, and that is the whole point: bKash prints amounts two different ways,
/// drops a space before "Priyo", sends a multi-line bill receipt, and issues the same
/// transaction id twice for a reserved-then-completed payment. None of that would have
/// been invented.
///
/// When a message arrives that the parser does not recognise, it goes in the corpus
/// first and the pattern is written second.
/// </summary>
public class BkashParserTests
{
    private static readonly MessageParser Parser = MessageParser.ForBkash();

    public static TheoryData<MessageKind, string> Corpus()
    {
        var data = new TheoryData<MessageKind, string>();

        foreach (var line in File.ReadAllLines(Path.Combine("fixtures", "corpus.tsv")))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('\t', 2);
            if (parts.Length == 2)
            {
                data.Add(Enum.Parse<MessageKind>(parts[0]), parts[1].Replace("\\n", "\n"));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Every_corpus_message_reads_as_its_recorded_kind(MessageKind expected, string body)
    {
        var result = Parser.Parse("bKash", body);

        Assert.Equal(expected, result.Kind);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Every_credit_carries_the_three_fields_needed_to_settle(MessageKind expected, string body)
    {
        if (!expected.IsCredit())
        {
            return;
        }

        var result = Parser.Parse("bKash", body);

        Assert.NotNull(result.Amount);
        Assert.NotNull(result.TrxId);
        Assert.NotNull(result.OccurredAt);
        Assert.True(result.Confidence >= 0.80m);
    }

    [Fact]
    public void A_message_from_an_unknown_sender_is_not_even_read()
    {
        // Anyone can send a text that looks exactly like this one. The sender id is the
        // only thing separating it from the real message on the handset.
        const string body =
            "You have received Tk 200.00 from 01910126335. Fee Tk 0.00. " +
            "Balance Tk 1,881.86. TrxID DHD6EYO3HO at 13/08/2026 16:02";

        var result = Parser.Parse("BKASH-PROMO", body);

        Assert.Equal(MessageKind.Unknown, result.Kind);
        Assert.Equal(0m, result.Confidence);
    }

    [Fact]
    public void An_otp_is_never_read_as_a_payment()
    {
        // It contains the word PAYMENT and an amount. Template order is what saves us.
        var result = Parser.Parse(
            "bKash",
            "Do NOT share your OTP or PIN with anyone. Your bKash OTP for PAYMENT " +
            "of Tk.50.00 to Term Paper BD_01627147784 is 824206. Expires in 2 min.");

        Assert.Equal(MessageKind.Otp, result.Kind);
        Assert.False(result.Kind.IsCredit());
    }

    [Fact]
    public void A_reserved_payment_and_its_completion_share_one_transaction_id()
    {
        // This pair is why the hold needs a template of its own. Treated as two ordinary
        // messages they look like one transaction id arriving twice, which is exactly the
        // signature of a replay.
        const string trxId = "DI162Q01ZM";

        var reserved = Parser.Parse(
            "bKash",
            $"Payment of Tk 416.00 is being reserved for ROBI AXIATA LIMITED-RM9564. " +
            $"Balance Tk 14,077.66. TrxID {trxId} at 01/09/2026 20:51");

        var completed = Parser.Parse(
            "bKash",
            $"Payment of Tk 416.00 to ROBI AXIATA LIMITED-RM9564 is successful. " +
            $"Balance Tk 14,077.66. TrxID {trxId} at 01/09/2026 20:51");

        Assert.Equal(MessageKind.PaymentReserved, reserved.Kind);
        Assert.Equal(MessageKind.PaymentSent, completed.Kind);
        Assert.Equal(trxId, completed.TrxId);
    }

    [Fact]
    public void A_send_money_reference_is_captured_when_the_customer_typed_one()
    {
        var result = Parser.Parse(
            "bKash",
            "You have received Tk 550.00 from 01780780720. Ref Ohidul. Fee Tk 0.00. " +
            "Balance Tk 19,509.78. TrxID DGP5P2OD65 at 25/07/2026 09:16");

        Assert.Equal(MessageKind.P2PReceived, result.Kind);
        Assert.Equal("Ohidul", result.Reference);
        Assert.Equal(550.00m, result.Amount);
    }

    [Fact]
    public void Timestamps_are_read_as_Dhaka_time_not_UTC()
    {
        // Six hours of drift would break every window comparison in the matcher.
        var result = Parser.Parse(
            "bKash",
            "You have received Tk 200.00 from 01910126335. Fee Tk 0.00. " +
            "Balance Tk 1,881.86. TrxID DHD6EYO3HO at 13/08/2026 16:02");

        // 16:02 in Dhaka is 10:02 UTC. The assertion is about the instant, because the
        // instant is what the matcher compares and what the database stores.
        Assert.Equal(
            new DateTimeOffset(2026, 8, 13, 10, 2, 0, TimeSpan.Zero),
            result.OccurredAt);

        Assert.Equal(16, result.OccurredAt!.Value.ToOffset(TimeSpan.FromHours(6)).Hour);
    }

    [Fact]
    public void Timestamps_leave_the_parser_with_a_zero_offset()
    {
        // Not a style preference. Npgsql refuses to write a DateTimeOffset with a non-zero
        // offset to a timestamptz column - "only offset 0 (UTC) is supported" - and it
        // throws that from inside SaveChanges, wrapped in a DbUpdateException whose own
        // message says nothing. Every payment failed on this, and the dashboard showed it
        // as a message nobody could parse, which it was not.
        //
        // An earlier version of the test above asserted Offset == +06:00: it checked how
        // the value was written rather than which instant it named, and that is precisely
        // why it let this through.
        var result = Parser.Parse(
            "bKash",
            "You have received Tk 200.00 from 01910126335. Fee Tk 0.00. " +
            "Balance Tk 1,881.86. TrxID DHD6EYO3HO at 13/08/2026 16:02");

        Assert.Equal(TimeSpan.Zero, result.OccurredAt!.Value.Offset);
    }

    [Theory]
    [InlineData("01/01/2026 00:00")]
    [InlineData("24/09/2026 22:27")]
    [InlineData("31/12/2026 23:59")]
    public void Every_timestamp_the_reader_produces_is_UTC(string stamp)
    {
        Assert.Equal(TimeSpan.Zero, BkashFieldReader.ReadTimestamp(stamp)!.Value.Offset);
    }

    [Fact]
    public void Thousands_separators_do_not_shrink_an_amount()
    {
        var result = Parser.Parse(
            "bKash",
            "You have received deposit from iBanking of Tk 15,000.00 from BRAC Bank " +
            "Internet Banking. Fee Tk 0.00. Balance Tk 15,002.91. TrxID DI192I8XYT at 01/09/2026 18:12");

        Assert.Equal(15000.00m, result.Amount);
    }
}
