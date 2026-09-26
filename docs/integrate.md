# Adding YoPay to your own .NET project

Fifteen minutes, on one laptop, with nothing deployed.

## 1. Start YoPay

```powershell
cd D:\YoPay
docker compose up -d
.\scripts\run-all.ps1
```

Dashboard: <http://localhost:5080/dashboard>. Seed the demo merchant if the button is
there.

## 2. Let webhooks reach your laptop

YoPay refuses to POST to a private address. That is not an oversight — a merchant-supplied
URL pointing at `127.0.0.1` or `169.254.169.254` would make this server fetch its own
internals and hand the answer back on a delivery row, which is server-side request forgery
by construction.

On your own machine that guard is exactly in the way, so there is one switch:

```powershell
$env:Webhooks__AllowPrivateEndpoints = "true"
.\scripts\run-all.ps1
```

Both hosts log a warning on every start while it is on. **Never set it on a deployed
instance.** The alternative — editing the guard by hand to get through an afternoon — is
how that edit reaches production permanently.

## 3. Get an API key

Dashboard → **Issue API key**. The `KeyId` and `Secret` are shown once. Put them where
they are not committed:

```powershell
cd path\to\YourProject
dotnet user-secrets init
dotnet user-secrets set "YoPay:KeyId" "k_live_..."
dotnet user-secrets set "YoPay:Secret" "the-secret"
dotnet user-secrets set "YoPay:BaseUrl" "http://localhost:5080"
```

## 4. Reference the SDK

```xml
<ProjectReference Include="..\YoPay\sdk\dotnet\YoPay.Sdk\YoPay.Sdk.csproj" />
```

```csharp
builder.Services.AddHttpClient<YoPayClient>(c => c.Timeout = TimeSpan.FromSeconds(20))
    .AddTypedClient((http, sp) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        return new YoPayClient(http, config["YoPay:KeyId"]!, config["YoPay:Secret"]!,
            config["YoPay:BaseUrl"]!);
    });
```

## 5. Create an invoice

```csharp
var invoice = await yopay.CreateInvoiceAsync(new CreateInvoiceRequest
{
    OrderRef = order.Number,
    Amount = order.Total,
    CustomerMsisdn = order.Phone,
});

return Redirect(invoice.CheckoutUrl!);
```

Show **`ChargedAmount`**, never `Amount`. YoPay may move the figure by a few poisha so the
incoming amount is unambiguous on that wallet; `ChargedAmount` is what the customer must
send, and anything else will not match.

Calling create again with the same `OrderRef` returns the invoice you already have. A
double-clicked button is harmless and no lock is needed.

## 6. Receive the webhook

Register your endpoint in the dashboard — `http://localhost:5000/yopay-webhook` while the
switch above is on. **The signing secret is shown once**; nothing can show it again.

```powershell
dotnet user-secrets set "YoPay:WebhookSecret" "the-secret-shown-once"
```

```csharp
app.MapPost("/yopay-webhook", async (HttpRequest request, YoPayClient yopay, IConfiguration config) =>
{
    request.EnableBuffering();
    using var reader = new StreamReader(request.Body, leaveOpen: true);
    var body = await reader.ReadToEndAsync();
    request.Body.Position = 0;

    if (!YoPaySignature.VerifyWebhook(
            config["YoPay:WebhookSecret"]!,
            request.Headers[YoPaySignature.SignatureHeader].ToString(),
            body))
    {
        return Results.Unauthorized();
    }

    var payload = JsonSerializer.Deserialize<WebhookPayload>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    var status = await yopay.VerifyAsync(orderRef: payload.OrderRef);

    if (status.IsPaid)
    {
        await orders.FulfilOnceAsync(payload.OrderRef, status.TrxId);
    }

    return Results.Ok();
});
```

Three things in there are not optional:

**Read the raw body.** Bind it to a model and serialise it again and the whitespace
changes; the MAC is over bytes. This is the single most common reason a correct-looking
integration reports that signatures never verify.

**Verify with the server anyway.** The signature proves the message came from YoPay.
`VerifyAsync` proves the payment is real. Only one of those is worth shipping goods on,
and it is the second.

**Fulfil once.** The same notification can arrive twice — a network failure after your
server committed looks identical to one before it, so YoPay promises at least once and
never exactly once. Key it on your own order.

`PendingReview` is not a failure. The window elapsed with nothing matched yet; operator
messages run minutes late and an offline handset uploads hours late. Do not cancel the
order on it.

## 7. Try it

Dashboard → create an invoice → **pay it**. That button writes the same two rows a real
customer and a real handset would, and then gets out of the way: the real parser reads the
message, the real matcher decides, the real state machine moves the invoice. A few seconds
later the invoice is `Paid`, the delivery shows `Succeeded 200`, and your order is
fulfilled.

If it does not arrive, the dashboard says why — under the message for a parsing or
matching problem, and in the deliveries table for a webhook one, with the response code
and the error. Dead-lettered deliveries have a **replay** button.

## A complete example

`samples/DemoShop` is a shop in one file doing all of the above. It references the SDK and
nothing else of YoPay's — if it ever needed an internal type, that would be a hole in the
SDK rather than a note in a README.

```powershell
cd D:\YoPay\samples\DemoShop
dotnet user-secrets set "YoPay:KeyId" "k_live_..."
dotnet user-secrets set "YoPay:Secret" "..."
dotnet user-secrets set "YoPay:WebhookSecret" "..."
dotnet run
```

<http://localhost:5090>. Register `http://localhost:5090/yopay-webhook` in the dashboard
first.

## Going to production later

- `Webhooks__AllowPrivateEndpoints` off, and your endpoint on `https://`.
- `YoPay:BaseUrl` pointing at the deployed API.
- Key, secret and webhook secret in environment variables or a vault, never in
  `appsettings.json`.
- Your webhook handler answering in well under fifteen seconds, doing the slow work after
  it has answered.
