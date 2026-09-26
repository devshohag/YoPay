using System.Collections.Concurrent;
using System.Text.Json;
using YoPay.Sdk;

// A shop, in one file, doing the four things every YoPay integration does: create an
// invoice, send the customer to the checkout, receive the webhook, and confirm with the
// server before parting with goods.
//
// It references the SDK and nothing else of YoPay's, on purpose. If this sample ever
// needs an internal type, that is a hole in the SDK rather than a note in the README.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<YoPayClient>(client => client.Timeout = TimeSpan.FromSeconds(20))
    .AddTypedClient((http, provider) =>
    {
        var config = provider.GetRequiredService<IConfiguration>();

        return new YoPayClient(
            http,
            config["YoPay:KeyId"] ?? throw new InvalidOperationException("YoPay:KeyId is not set."),
            config["YoPay:Secret"] ?? throw new InvalidOperationException("YoPay:Secret is not set."),
            config["YoPay:BaseUrl"] ?? "https://api.yopay.com.bd");
    });

builder.Services.AddSingleton<Orders>();

var app = builder.Build();

// ---------------------------------------------------------------------------
// The shop
// ---------------------------------------------------------------------------

app.MapGet("/", (Orders orders) => Results.Content(Pages.Home(orders.All()), "text/html"));

app.MapPost("/buy", async (
    HttpRequest http, YoPayClient yopay, Orders orders, ILogger<Program> log) =>
{
    // The form is read by hand rather than bound to parameters. Minimal APIs run
    // antiforgery validation as soon as they bind a form, and a sample that answers 400
    // before it reaches any YoPay code teaches nothing. A real shop keeps the token.
    var form = await http.ReadFormAsync();

    if (!decimal.TryParse(form["amount"], out var amount) || amount <= 0)
    {
        return Results.Content(Pages.Trouble("Enter an amount."), "text/html");
    }

    var phone = form["phone"].ToString();
    var order = orders.Place(amount);

    try
    {
        var invoice = await yopay.CreateInvoiceAsync(new CreateInvoiceRequest
        {
            OrderRef = order.Reference,
            Amount = order.Total,
            CustomerMsisdn = string.IsNullOrWhiteSpace(phone) ? null : phone,
            RedirectUrl = "http://localhost:5090/",
            MetadataJson = JsonSerializer.Serialize(new { orderId = order.Reference }),
        });

        // ChargedAmount, never Amount. YoPay may adjust the figure by a few poisha so the
        // incoming amount is unambiguous on that wallet, and this is what the customer
        // must actually send. Show them Amount and the payment will not match.
        orders.Awaiting(order.Reference, invoice.InvoiceId, invoice.ChargedAmount);

        return Results.Redirect(invoice.CheckoutUrl!);
    }
    catch (YoPayException ex) when (ex.Unreachable)
    {
        // Nothing answered, so the invoice may or may not exist. Retrying the same
        // reference is safe - create is idempotent on it - so the order stays placed and
        // the customer can try again.
        log.LogWarning("YoPay unreachable: {Message}", ex.Message);
        return Results.Content(Pages.Trouble("YoPay could not be reached. Please try again."), "text/html");
    }
    catch (YoPayException ex)
    {
        log.LogWarning("YoPay refused: {Status} {Message}", ex.Status, ex.Message);
        return Results.Content(Pages.Trouble(ex.Message), "text/html");
    }
});

// ---------------------------------------------------------------------------
// The webhook
// ---------------------------------------------------------------------------

app.MapPost("/yopay-webhook", async (
    HttpRequest request,
    YoPayClient yopay,
    Orders orders,
    IConfiguration config,
    ILogger<Program> log) =>
{
    // The raw bytes, exactly as they arrived.
    //
    // This is the step almost everyone gets wrong. Bind to a model and serialise it again
    // and the whitespace changes; the MAC is over bytes, so the signature will never
    // verify and nothing will explain why.
    request.EnableBuffering();
    using var reader = new StreamReader(request.Body, leaveOpen: true);
    var body = await reader.ReadToEndAsync();
    request.Body.Position = 0;

    var header = request.Headers[YoPaySignature.SignatureHeader].ToString();
    var secret = config["YoPay:WebhookSecret"] ?? "";

    if (!YoPaySignature.VerifyWebhook(secret, header, body))
    {
        // Your endpoint is a public URL. Anyone who finds it can POST a body that says an
        // invoice is paid; this line is what stops that being a free order.
        log.LogWarning("Rejected a webhook whose signature did not verify.");
        return Results.Unauthorized();
    }

    var payload = JsonSerializer.Deserialize<WebhookPayload>(
        body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    log.LogInformation("Webhook {Event} for {OrderRef}", payload.Event, payload.OrderRef);

    // Confirm with the server before fulfilling. The webhook told us to look; this is the
    // record. Verifying the signature proves the message came from YoPay - it does not
    // prove the payment is real, and only one of those two is worth shipping goods on.
    var status = await yopay.VerifyAsync(orderRef: payload.OrderRef);

    if (status.IsPaid)
    {
        // Idempotent on purpose. The same notification can arrive twice - a network
        // failure after our server committed looks exactly like one before it - so YoPay
        // promises at least once and never exactly once.
        orders.FulfilOnce(payload.OrderRef, status.TrxId);
    }

    // 200 quickly. The request times out at 15 seconds, and a handler that sends an email
    // first gets retried while the first attempt is still running.
    return Results.Ok();
});

app.Run();

// ---------------------------------------------------------------------------

internal sealed record Order(string Reference, decimal Total)
{
    public string State { get; set; } = "Placed";
    public decimal? Charged { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? TrxId { get; set; }
}

internal sealed class Orders
{
    private readonly ConcurrentDictionary<string, Order> _orders = new();
    private int _next = 1040;

    public IReadOnlyCollection<Order> All() =>
        _orders.Values.OrderByDescending(o => o.Reference).ToList();

    public Order Place(decimal total)
    {
        var order = new Order($"ORD-{Interlocked.Increment(ref _next)}", total);
        _orders[order.Reference] = order;

        return order;
    }

    public void Awaiting(string reference, Guid invoiceId, decimal charged)
    {
        if (_orders.TryGetValue(reference, out var order))
        {
            order.InvoiceId = invoiceId;
            order.Charged = charged;
            order.State = "AwaitingPayment";
        }
    }

    /// <summary>
    /// Fulfils once, whatever happens. This is the whole defence against at-least-once
    /// delivery, and in a real shop it would be a conditional UPDATE rather than a lock -
    /// two webhook retries can land on two servers at the same moment.
    /// </summary>
    public void FulfilOnce(string reference, string? trxId)
    {
        if (!_orders.TryGetValue(reference, out var order))
        {
            return;
        }

        lock (order)
        {
            if (order.State == "Fulfilled")
            {
                return;
            }

            order.TrxId = trxId;
            order.State = "Fulfilled";
        }
    }
}
