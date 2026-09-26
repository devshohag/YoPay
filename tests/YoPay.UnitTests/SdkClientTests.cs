using System.Net;
using System.Text.Json;
using YoPay.Sdk;

namespace YoPay.UnitTests;

/// <summary>
/// The .NET SDK, against a stub server.
///
/// It reimplements the signatures rather than referencing the platform's own code - it
/// has to, or every merchant integrating would be coupled to our internals and we could
/// never refactor them. Two implementations of one MAC drift silently, and the symptom is
/// unreadable: every call starts answering 401 and neither side looks wrong. So the SDK
/// is tested from the same run as the server, and the last test here checks the SDK
/// against signatures the server generated.
/// </summary>
public class SdkClientTests
{
    private const string Secret = "sxJx3Xp1G0s1kM8oZ4qYv2mB6nC9dF7hK0lP3rT5uW8=";

    private const string InvoiceJson = """
        {"invoiceId":"0192a0f0-0000-7000-8000-000000000001","orderRef":"ORD-1041",
         "amount":500.00,"chargedAmount":500.13,"currency":"BDT","status":"AwaitingPayment",
         "expiresAt":"2026-09-25T10:25:00+00:00","checkoutUrl":"https://pay.example.com/pay/x"}
        """;

    private static (YoPayClient Client, StubServer Server) Build()
    {
        var server = new StubServer();
        var client = new YoPayClient(new HttpClient(server), "k_live_1", Secret, "https://api.example.com");

        return (client, server);
    }

    [Fact]
    public async Task An_invoice_comes_back_parsed()
    {
        var (client, server) = Build();
        server.Reply = () => (HttpStatusCode.Created, InvoiceJson);

        var invoice = await client.CreateInvoiceAsync(new CreateInvoiceRequest
        {
            OrderRef = "ORD-1041",
            Amount = 500.00m,
        });

        Assert.Equal("ORD-1041", invoice.OrderRef);
        Assert.Equal(500.13m, invoice.ChargedAmount);
        Assert.False(invoice.IsPaid);
    }

    [Fact]
    public async Task Every_call_carries_the_four_signing_headers()
    {
        var (client, server) = Build();
        server.Reply = () => (HttpStatusCode.Created, InvoiceJson);

        await client.CreateInvoiceAsync(new CreateInvoiceRequest { OrderRef = "ORD-1", Amount = 1m });

        Assert.Contains(YoPaySignature.KeyHeader, server.HeaderNames);
        Assert.Contains(YoPaySignature.TimestampHeader, server.HeaderNames);
        Assert.Contains(YoPaySignature.NonceHeader, server.HeaderNames);
        Assert.Contains(YoPaySignature.SignatureHeader, server.HeaderNames);
    }

    [Fact]
    public async Task The_signature_sent_is_the_one_the_server_would_compute()
    {
        // The test that matters. Everything else about the client can be right and this
        // one thing wrong, and the result is an integration where nothing works and
        // nothing explains itself.
        var (client, server) = Build();
        server.Reply = () => (HttpStatusCode.Created, InvoiceJson);

        await client.CreateInvoiceAsync(new CreateInvoiceRequest { OrderRef = "ORD-1", Amount = 1m });

        var timestamp = long.Parse(server.Header(YoPaySignature.TimestampHeader));
        var nonce = server.Header(YoPaySignature.NonceHeader);

        var expected = YoPaySignature.SignRequest(
            Secret, "POST", "/v1/payment/create", timestamp, nonce, server.Body!);

        Assert.Equal(expected, server.Header(YoPaySignature.SignatureHeader), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_call_uses_a_fresh_nonce()
    {
        // The server refuses a nonce it has seen. Reusing one would make the second call
        // of every pair fail, which is the kind of bug that looks like a flaky network.
        var (client, server) = Build();
        server.Reply = () => (HttpStatusCode.Created, InvoiceJson);

        await client.CreateInvoiceAsync(new CreateInvoiceRequest { OrderRef = "ORD-1", Amount = 1m });
        var first = server.Header(YoPaySignature.NonceHeader);

        await client.CreateInvoiceAsync(new CreateInvoiceRequest { OrderRef = "ORD-2", Amount = 1m });

        Assert.NotEqual(first, server.Header(YoPaySignature.NonceHeader));
    }

    [Fact]
    public async Task Fields_that_were_not_set_are_left_out_of_the_body()
    {
        var (client, server) = Build();
        server.Reply = () => (HttpStatusCode.Created, InvoiceJson);

        await client.CreateInvoiceAsync(new CreateInvoiceRequest { OrderRef = "ORD-1", Amount = 1m });

        Assert.DoesNotContain("customerName", server.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_reports_a_paid_invoice()
    {
        var (client, server) = Build();
        server.Reply = () => (HttpStatusCode.OK, """
            {"invoiceId":"0192a0f0-0000-7000-8000-000000000001","status":"Paid",
             "receivedAmount":500.13,"paidAt":"2026-09-25T10:06:00+00:00","trxId":"DHD6EYO3HO"}
            """);

        var status = await client.VerifyAsync(orderRef: "ORD-1041");

        Assert.True(status.IsPaid);
        Assert.Equal("DHD6EYO3HO", status.TrxId);
    }

    [Fact]
    public async Task A_refusal_surfaces_the_servers_own_sentence()
    {
        var (client, server) = Build();
        server.Reply = () => (HttpStatusCode.Conflict, """{"title":"This invoice has been paid."}""");

        var error = await Assert.ThrowsAsync<YoPayException>(
            () => client.CancelAsync(orderRef: "ORD-1041"));

        Assert.Equal(409, error.Status);
        Assert.False(error.Unreachable);
        Assert.Contains("has been paid", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dead_network_is_reported_as_unknown_rather_than_as_refusal()
    {
        // The distinction a caller has to act on. A 400 means try something different;
        // this means try the same thing again, which is safe because create and cancel are
        // idempotent on the order reference.
        var (client, server) = Build();
        server.Throw = true;

        var error = await Assert.ThrowsAsync<YoPayException>(
            () => client.VerifyAsync(orderRef: "ORD-1041"));

        Assert.True(error.Unreachable);
        Assert.Equal(0, error.Status);
    }

    [Fact]
    public void The_sdk_agrees_with_the_server_on_every_recorded_signature()
    {
        var json = File.ReadAllText(Path.Combine("fixtures", "vectors.json"));
        using var document = JsonDocument.Parse(json);

        var secret = document.RootElement.GetProperty("secret").GetString()!;
        var checkedAny = false;

        foreach (var vector in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var body = vector.GetProperty("body").GetString()!;
            var ts = vector.GetProperty("ts").GetInt64();

            if (vector.GetProperty("kind").GetString() == "request")
            {
                var signature = YoPaySignature.SignRequest(
                    secret,
                    vector.GetProperty("method").GetString()!,
                    vector.GetProperty("path").GetString()!,
                    ts,
                    vector.GetProperty("nonce").GetString()!,
                    body);

                Assert.Equal(
                    vector.GetProperty("signature").GetString(), signature, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Assert.True(YoPaySignature.VerifyWebhook(
                    secret,
                    vector.GetProperty("header").GetString()!,
                    body,
                    DateTimeOffset.FromUnixTimeSeconds(ts)));
            }

            checkedAny = true;
        }

        Assert.True(checkedAny, "The vector file had nothing in it.");
    }

    private sealed class StubServer : HttpMessageHandler
    {
        public Func<(HttpStatusCode, string)> Reply { get; set; } = () => (HttpStatusCode.OK, "{}");
        public bool Throw { get; set; }

        public string? Body { get; private set; }
        public string HeaderNames { get; private set; } = string.Empty;

        private HttpRequestMessage? _last;

        public string Header(string name) => _last!.Headers.GetValues(name).First();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new HttpRequestException("connection refused");
            }

            _last = request;
            HeaderNames = string.Join(",", request.Headers.Select(h => h.Key));

            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var (code, body) = Reply();

            return new HttpResponseMessage(code) { Content = new StringContent(body) };
        }
    }
}
