# Running the phone trial

Getting a real handset to talk to YoPay running on a laptop. Everything here is local —
nothing is exposed to the internet and nothing needs a domain or a certificate.

## The ports

| Service | Port | Health |
|---|---|---|
| `YoPay.Api` (merchant API + dashboard) | 5080 | `/dashboard` |
| `YoPay.Ingest.Api` (**the one the app talks to**) | 5081 | `/health` |
| `YoPay.Checkout` | 5082 | `/health` |
| `YoPay.Worker` | 5083 | `/health` |

The app only ever needs 5081. The rest are for you.

## Which network

The phone and the laptop have to be on the same local network. There are three ways, and
the difference between them is not convenience — two of them work and one cannot.

**Shared wifi.** Both on the same router. Simplest when a router is there. Some routers
run client isolation, which silently blocks device-to-device traffic; if the health check
below fails on wifi that is otherwise fine, this is why, and no amount of firewall work
will fix it.

**The phone's hotspot.** The laptop joins the hotspot as a client. Works with no router at
all, and there is no client isolation to worry about. The laptop's internet then comes off
mobile data, so build and `docker compose up -d` **before** switching over — a NuGet
restore on mobile data is an expensive way to discover this.

**Mobile data, with the phone on the carrier network and the laptop elsewhere.** This
cannot work. `192.168.x.x` is a private address that only means anything inside one
network; from the carrier's side, behind its NAT, the laptop does not exist. Port
forwarding on the router does not help either, because the carrier NATs as well. If you
need this, you need a tunnel — and for a local trial you do not need it.

## Steps

1. `docker compose up -d` and `dotnet build`, while you still have ordinary internet.
2. Put the phone and the laptop on the same network (hotspot: turn it on, then join it
   from the laptop).
3. `.\scripts\run-for-phone.ps1`

   It finds the address, starts all four services on `0.0.0.0`, tells you about anything
   still in the way, and then proves the address answers before you touch the phone.
4. Do what it tells you, as Administrator, and run it again. Usually that is one or both
   of:

   ```powershell
   Set-NetConnectionProfile -Name '<network>' -NetworkCategory Private

   New-NetFirewallRule -DisplayName 'YoPay local trial' -Direction Inbound `
     -Action Allow -Protocol TCP -LocalPort 5080,5081,5082,5083 -Profile Any
   ```
5. Once it prints `reachable on <address>:5081`, open `http://<address>:5081/health` in
   the **phone's** browser. JSON means the network is done.
6. Put `http://<address>:5081` into the app's Server address field.

The address changes whenever you reconnect. Re-run the script and retype it in the app;
nothing caches it for you.

## The two failures that look like something else

Both of these end with the phone saying it cannot reach the site, and both send people to
look at the port and the IP address, which are fine.

**The network is Public.** Windows files every network it has not seen before as Public,
and refuses inbound connections on Public regardless of a firewall rule written for
Private. A hotspot is a new network every time, so this is the normal case rather than the
unusual one. The script names the network and gives you the command.

**The app is a release build.** Android has blocked cleartext HTTP since API 28, and the
trial server is plain `http://` with no certificate. `app/src/debug/AndroidManifest.xml`
turns it back on **for the debug variant only** — so the debug APK works and the release
APK does not. The release build must never carry it: a payment app that accepts cleartext
on a merchant's phone is one hostile wifi away from someone editing a payment in flight.

This one is nastier than it sounds, because the browser check in step 5 passes. The
browser has no such restriction. So the network is proven, the app still fails, and the
app reports a bare network error. If step 5 passed and the app cannot connect, check which
APK is on the phone before you check anything else.

## When the trial is over

```powershell
Remove-NetFirewallRule -DisplayName 'YoPay local trial'
```

Leaving it is not a disaster — it only opens those ports while a service is listening —
but a firewall hole that outlives its reason is how the next one gets justified.
