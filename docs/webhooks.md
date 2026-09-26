# Webhooks

When a payment settles, YoPay POSTs a signed JSON body to every endpoint the merchant has
registered. This is how a shop finds out it has been paid.

## The request

```
POST https://shop.example.com/yopay
Content-Type: application/json
X-YoPay-Event: payment.paid
X-YoPay-Delivery: 019278f4-...        an id that stays the same across retries
X-YoPay-Signature: t=1790269500,v1=6e4b59d6...
```

```json
{
  "event": "payment.paid",
  "invoiceId": "0192...",
  "orderRef": "ORD-1041",
  "status": "Paid",
  "amount": 500.00,
  "receivedAmount": 500.00,
  "trxId": "DHD6EYO3HO",
  "paidAt": "2026-09-24T17:05:00+00:00",
  "sentAt": "2026-09-24T17:05:04+00:00",
  "metadataJson": null
}
```

Events: `payment.paid`, `payment.partial`, `payment.expired`, `payment.pending_review`.

## Verifying — not optional

The endpoint is a public URL. Anyone who finds it can POST a body that says an invoice is
paid, and a shop that trusts the body ships goods for free. The signature is the only
thing preventing that.

`v1` is an HMAC-SHA256, hex encoded, over the string `"{t}.{raw request body}"`, keyed
with the endpoint's signing secret. Compute it over the **raw bytes** as received —
deserialising and re-serialising the JSON changes the whitespace and the MAC will not
match.

Reject a delivery whose `t` is more than five minutes from your clock. The timestamp is
inside the MAC, so it cannot be rewritten, but if you ignore it a captured delivery can be
replayed against you forever.

```php
function yopay_verify(string $secret, string $header, string $body): bool {
    $parts = [];
    foreach (explode(',', $header) as $piece) {
        [$k, $v] = array_pad(explode('=', trim($piece), 2), 2, '');
        $parts[$k] = $v;
    }

    if (!isset($parts['t'], $parts['v1'])) return false;
    if (abs(time() - (int) $parts['t']) > 300) return false;

    $expected = hash_hmac('sha256', $parts['t'] . '.' . $body, $secret);

    return hash_equals($expected, $parts['v1']);
}
```

## Delivery, retries and what "at least once" means

Answer with any `2xx`. Anything else is a failure.

Retries run at 1 minute, 5, 30, 2 hours and 12 hours, then the delivery is dead-lettered
and shown on the dashboard, where it can be replayed by hand. A `404`, `410`, `401` or
`403` is not retried at all: those do not change in five minutes.

**The same notification can arrive more than once.** A network failure after your server
committed but before the response reached us looks identical to a failure before it. Key
your handling on `X-YoPay-Delivery` or on `invoiceId`, and make a repeat a no-op. YoPay
promises at least once, never exactly once, because nothing that crosses a network can
promise the second thing.

Answer quickly — do the work afterwards. The request times out at 15 seconds, and a
handler that sends an email before responding will eventually be retried while the first
attempt is still running.

## Endpoint requirements

`https://` only, on port 443 or 8443, resolving to a public address. Private, loopback,
link-local and cloud-metadata addresses are refused when the URL is saved and again at
connection time, and redirects are not followed — a `302` to `169.254.169.254` would
otherwise walk straight past the first check.

The signing secret is shown once when the endpoint is registered. No screen can show it
again; register the endpoint afresh if it is lost.
