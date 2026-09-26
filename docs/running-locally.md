# Running YoPay locally

Everything below assumes the repository is at `D:\YoPay`.

## 1. A key ring

`scripts\run-all.ps1` reads `local.ps1` and starts every service from it. That file is
gitignored; copy the example and put a key in it:

```powershell
cd D:\YoPay
Copy-Item local.ps1.example local.ps1
notepad local.ps1
```

The API refuses to start without a key ring, on purpose: a payment service that boots with
no way to decrypt its own credentials looks healthy right up until the first real request.

## 2. Database

```powershell
docker compose up -d
dotnet ef database update -p src\YoPay.Infrastructure -s src\YoPay.Infrastructure
```

Postgres is on **5433**, not 5432, so it never collides with a local install or YoMail's.

## 3. Run it

```powershell
.\scripts\run-all.ps1
```

Four windows: Api (5080), Ingest (5081), Checkout (5082), Worker (5083). The script stops
any running `dotnet` first — a previous run holding the build output is otherwise answered
with a file lock and nothing that explains itself.

Dashboard: <http://localhost:5080/dashboard>. Development only; it has no authentication
and reads every merchant's data, so the day it is reachable from anywhere but a laptop is
the day it becomes the worst hole in the product.

## 4. A merchant and a payment

On the dashboard:

1. **Seed demo merchant** if the button is there.
2. **Create** an invoice.
3. **pay it**.

That button writes the same two rows a real customer and a real handset would — a claim
and a raw message — and then gets out of the way. The real parser reads the message, the
real matcher decides, the real state machine moves the invoice. Nothing about it shortcuts
to the answer, which is the only reason it is worth testing with.

A few seconds later the invoice is **Paid**.

**Or the whole way round:** press **open** on an invoice to see the hosted checkout page —
that is what a customer actually gets. Type the transaction id there, then send the
message from the dashboard.

## 5. Webhooks

Register an endpoint on the dashboard. For a shop on this same laptop, set
`Webhooks__AllowPrivateEndpoints` in `local.ps1` first — the outbound guard refuses
private addresses by design, and that is not an oversight. Both hosts log a warning on
every start while it is on.

The signing secret is shown once and nothing can show it again.

## 6. Your own application

`docs\integrate.md` is the fifteen-minute version. `samples\DemoShop` is a shop in one
file doing the whole loop; `sdk\dotnet` and `sdk\php` are the clients.

## When something does not land

The dashboard says why, which is the point of it:

- **Incoming messages** — a message that could not be read or could not be matched carries
  the reason underneath it, in the matcher's own words.
- **Invoices → Window** — `closed` in red means a payment arriving now would be refused,
  whatever else is right.
- **Webhook deliveries** — status, attempts, response code and the error. Dead letters have
  a **replay** button.

Worth trying, because each one is a rule rather than an accident:

- Call `/v1/payment/create` twice with the same `orderRef`. The second answers 200 with the
  first invoice, not a second one.
- Change one character of a signature. 401, and nothing is written to the nonce table.
- Send the same signed request twice. The second is refused as a replay.
- Register `https://127.0.0.1/hook` as a webhook with the switch off. Refused.

## Signing a request by hand

```powershell
$env:YOPAY_KEY_ID = '...'      # from the dashboard's Issue API key
$env:YOPAY_SECRET = '...'

.\scripts\Invoke-YoPay.ps1 -Path /v1/payment/create -Body @{
    orderRef = 'ORD-1001'
    amount   = 500
    method   = 1
}
```

That script builds the canonical string the server builds, so if an SDK ever disagrees
with it, the SDK is wrong.
