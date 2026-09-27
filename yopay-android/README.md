# YoPay device app

Captures the payment messages that arrive on a merchant's phone and uploads them, signed,
to the ingest service. It is the production version of what `tools/YoPay.DeviceSim` does.

## Open it

Android Studio, **Open**, pick this folder. Let it sync; it will offer to upgrade the
Gradle wrapper and the plugin versions, and accepting is fine.

## Point it at the server

- **Emulator:** `http://10.0.2.2:5081` - that address is the host machine from inside the
  emulator. It is the default.
- **Real phone:** the laptop's address on the same wifi, e.g. `http://192.168.0.105:5081`.
  Find it with `ipconfig`, and make sure Windows Firewall allows inbound 5081.

Cleartext HTTP works because `usesCleartextTraffic` defaults to true below API 28 targets
only - for a real phone on API 28+ add `android:usesCleartextTraffic="true"` to the
`<application>` tag while testing locally, and take it out before anything ships.

## Pair it

1. Dashboard at `http://localhost:5080/dashboard`, press **Pairing code**.
2. Type the code into the app, press **Pair**.
3. Press **Grant notification access** and switch YoPay on in the system list.

The permanent notification means it is running. The dashboard's Devices row shows the
heartbeat within a minute.

## What to watch during the trial

This is the part of the product nothing else can prove. On every handset, record:

- Does a bKash payment notification actually reach the listener, and does it carry the
  transaction id?
- Does it survive a reboot without anyone opening the app?
- Does it survive the manufacturer's battery optimisation, with and without the exemption?
- Aeroplane mode for two hours, then back: does every queued message arrive?
- Overnight with the screen off, then a payment at 3am: is it captured?
- Battery cost over 24 hours.

Samsung, Xiaomi or Redmi, Oppo or Realme, and something close to stock. Three of the four
passing is the bar; which one fails, and how, decides whether that handset is supported.

## Design notes

- The signing key is generated in the Android Keystore and never leaves it. A stolen
  server database contains no device's key.
- Signatures are DER, which is what `SHA256withECDSA` emits. The server accepts that and
  .NET's fixed-width form, so neither side re-encodes the other's output.
- Nothing is deleted from the queue until the server acknowledges it. Uploads are
  at-least-once; the server deduplicates on a hash it computes itself.
- Only whitelisted senders are captured, here and again on the server.
- Two dependencies, both AndroidX. No Room, no Retrofit, no Compose: every code generator
  and version-matched library is one more reason a merchant's phone stops reporting.
