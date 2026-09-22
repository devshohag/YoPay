# T0A - Android capture feasibility spike

Runs alongside T0, not after the backend. The listener is the single largest technical
risk in this product: if it cannot be kept alive reliably on the phones Bangladeshi
merchants actually own, no amount of backend correctness matters. Finding that out in
month one costs a week. Finding it out in month four costs the project.

**This is a throwaway.** It proves or disproves a capability. T10 writes the real app.

## Time box

Five working days. If the answer is not clear by then, that is itself the answer.

## Devices - at least four, different manufacturers

Two phones is not a test; every manufacturer breaks background work differently.

| Phone | Why it is on the list |
|---|---|
| Samsung | Largest share locally; its own battery manager |
| Xiaomi / Redmi | MIUI is the most aggressive killer of background services |
| Oppo / Realme / Vivo | ColorOS autostart is off by default and silent |
| Stock or Pixel-like Android | The control case - if it fails here, the code is wrong |

Record for each: manufacturer, model, Android version, skin version.

## What the spike must answer

1. **Capture** - does `NotificationListenerService` reliably receive bKash notifications?
   Record the exact payload: title, text, sub-text, timestamp, package name.
2. **Does the notification carry a transaction id at all?** If it does not, the whole
   design leans on SMS and T7 changes shape. Answer this on day one.
3. **Survives reboot** - listener reconnects without the user opening the app.
4. **Survives force-stop** - and if it does not, does the user find out?
5. **Survives battery optimisation** - both with and without the exemption granted.
6. **Permission revoked** - does the app notice within seconds and report it?
7. **Offline queue** - aeroplane mode for two hours, then reconnect: does every event
   arrive, in order, exactly once?
8. **Overnight run** - 12 hours idle, screen off, then a payment at 3am. Is it captured?
9. **Battery cost** - percentage per 24 hours at the heartbeat intervals in
   `HeartbeatPolicy`, measured, not guessed.
10. **Dual SIM / two wallets on one phone** - can the source be told apart?

## Pass criteria

- 100% capture over 48 hours on at least three of the four phones
- Median capture-to-upload under 10 seconds on a normal connection
- Reboot and offline recovery working on every phone tested
- Battery cost under 3% per 24 hours idle
- Every failure mode either detected and reported, or documented as undetectable

## Fail, and what it means

- **Notifications carry no transaction id** - SMS becomes primary, with everything that
  implies for store policy. T7 rewrites.
- **One OEM cannot be kept alive** - name it in the docs, exclude it at onboarding, and
  make the dashboard warn. Do not pretend it works.
- **Capture is unreliable everywhere** - the product does not work. Better to know now,
  with one week spent, than after the backend is finished.

## Output

A short written result, the captured message corpus (feeding T7's fixtures), and a
go / no-go with the conditions attached. The spike code is not merged.
