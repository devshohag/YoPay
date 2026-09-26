# YoPay .NET SDK

No package references. `net8.0` and newer.

```xml
<ProjectReference Include="..\path\to\sdk\dotnet\YoPay.Sdk\YoPay.Sdk.csproj" />
```

## Register it

```csharp
builder.Services.AddHttpClient<YoPayClient>(c => c.Timeout = TimeSpan.FromSeconds(20))
    .AddTypedClient((http, _) => new YoPayClient(
        http,
        builder.Configuration["YoPay:KeyId"]!,
        builder.Configuration["YoPay:Secret"]!,
        builder.Configuration["YoPay:BaseUrl"] ?? "https://api.yopay.com.bd"));
```

The key and secret belong in user secrets, environment variables or a vault — not in
`appsettings.json` that goes into git. Anyone holding them can create and read invoices as
you.

## Take a payment

```csharp
var invoice = await yopay.CreateInvoiceAsync(new CreateInvoiceRequest
{
    OrderRef = order.Number,
    Amount = order.Total,
    CustomerMsisdn = order.Phone,
    RedirectUrl = $"https://shop.example.com/thanks/{order.Number}",
    MetadataJson = JsonSerializer.Serialize(new { orderId = order.Id }),
});

return Redirect(invoice.CheckoutUrl!);
```

Calling this again with the same `OrderRef` returns the invoice you already have rather
than making a second one — a double-clicked checkout button is harmless and you do not
need a lock around it.

**Show `ChargedAmount`, never `Amount`.** YoPay may adjust the figure by a few poisha so
the incoming amount is unambiguous on that wallet, and `ChargedAmount` is what the
customer must actually send.

## Receive the result

```csharp
app.MapPost("/yopay-webhook", async (HttpRequest request, YoPayClient yopay, IConfiguration config) =>
{
    // The raw bytes. The MAC is over exactly what was sent, so binding to a model and
    // serialising it again will not verify - this is the single most common reason a
    // correct-looking integration reports that signatures never work.
    request.EnableBuffering();
    using var reader = new StreamReader(request.Body, leaveOpen: true);
    var body = await reader.ReadToEndAsync();
    request.Body.Position = 0;

    var header = request.Headers[YoPaySignature.SignatureHeader].ToString();

    if (!YoPaySignature.VerifyWebhook(config["YoPay:WebhookSecret"]!, header, body))
    {
        return Results.Unauthorized();
    }

    var payload = JsonSerializer.Deserialize<WebhookPayload>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    // Answer now, work afterwards. The request times out at 15 seconds, and a handler
    // that sends an email first gets retried while the first attempt is still running.
    _ = Task.Run(async () =>
    {
        var status = await yopay.VerifyAsync(orderRef: payload.OrderRef);

        if (status.IsPaid)
        {
            await orders.FulfilOnceAsync(payload.OrderRef, status.TrxId!);
        }
    });

    return Results.Ok();
});
```

Two separate things are happening and neither replaces the other. The signature proves the
message came from YoPay. `VerifyAsync` proves the payment is real. A shop that ships on the
webhook body alone ships on a POST that anyone who found the URL could have sent.

**The same notification can arrive twice.** A network failure after your server committed
looks identical to one before it, so YoPay promises at least once and never exactly once.
Make fulfilment idempotent — key it on your own order, or on the `X-YoPay-Delivery` header
— which is what `FulfilOnceAsync` is doing above.

`PendingReview` is not a failure. It means the window elapsed with nothing matched yet;
operator messages run minutes late and an offline handset uploads hours late. Do not
cancel the order on it.

## Cancel an abandoned cart

```csharp
await yopay.CancelAsync(orderRef: order.Number);
```

Every open invoice reserves one amount on the wallet. A shop that never cancels slowly
runs out of usable amounts.

## When a call fails

```csharp
try
{
    var invoice = await yopay.CreateInvoiceAsync(request);
}
catch (YoPayException ex) when (ex.Unreachable)
{
    // Nothing answered, so you do not know whether it worked. Retry the same OrderRef -
    // that is safe, and it is the right move.
}
catch (YoPayException ex)
{
    logger.LogWarning("YoPay refused: {Status} {Message}", ex.Status, ex.Message);
}
```

## On .NET Framework

`YoPaySignature.cs` has no dependencies beyond `System.Security.Cryptography` and compiles
on 4.6.2. Copy that one file and call the API yourself:

```csharp
var body = JsonConvert.SerializeObject(new { orderRef = "ORD-1041", amount = 500.00, method = "Bkash" });
var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var nonce = Guid.NewGuid().ToString("N");

var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1/payment/create")
{
    Content = new StringContent(body, Encoding.UTF8, "application/json"),
};

request.Headers.TryAddWithoutValidation("X-YoPay-Key", keyId);
request.Headers.TryAddWithoutValidation("X-YoPay-Timestamp", ts.ToString());
request.Headers.TryAddWithoutValidation("X-YoPay-Nonce", nonce);
request.Headers.TryAddWithoutValidation("X-YoPay-Signature",
    YoPaySignature.SignRequest(secret, "POST", "/v1/payment/create", ts, nonce, body));
```

The nonce must be different every time; the server refuses one it has seen before, and
that is what stops a captured request being replayed.

## Checking a port

Signatures are pinned by `sdk/php/tests/vectors.json`, generated by the server itself. If
you port this to another language, check against that file rather than against your
reading of the docs — it is how both SDKs are tested.
