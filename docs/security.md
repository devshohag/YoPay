# Security notes (T3)

## The key ring

`ISecretProtector` encrypts the values that have to be read back later - the merchant
HMAC secret above all, which cannot be hashed because signing needs the original.

Configuration, from a Docker secret or the environment:

    Security__ActiveKeyId=k1
    Security__Keys__k1=<openssl rand -base64 32>
    Security__Keys__k0=<the previous key>

Every ciphertext is `v1.{keyId}.{base64(nonce|ciphertext|tag)}`, and the key id is passed
to AES-GCM as associated data, so editing it in the database fails the tag check rather
than pointing the value at another key.

### Rotating a key

1. Generate a new key and add it as `Security__Keys__k2`, leaving the old one in place.
2. Set `Security__ActiveKeyId=k2` and restart. New writes use k2; everything written with
   k1 still reads.
3. Run the re-encryption sweep. `AesGcmSecretProtector.NeedsRotation(ciphertext)` says
   which rows are still on an old key.
4. Only once the sweep reports zero, remove `Security__Keys__k1`.

Removing a key before step 4 finishes makes the values written with it unreadable. The
protector says so explicitly when it happens, which is the difference between a clear
operational error and an afternoon of confusion.

## Request signing

Four headers: `X-YoPay-Key`, `X-YoPay-Timestamp`, `X-YoPay-Nonce`, `X-YoPay-Signature`.

The signature is HMAC-SHA256 over:

    METHOD \n PATH_AND_QUERY \n UNIX_TIMESTAMP \n NONCE \n SHA256_HEX(BODY)

Verification order is clock, then signature, then nonce - and that order is deliberate.
Consuming the nonce first would let anyone who can reach the endpoint fill the table
without holding a key, and would let them burn a nonce a real client was about to use.
There is a test asserting the nonce store is never touched when the signature is wrong.

The five minute clock window is not the replay defence. Inside that window the same
signed request would otherwise work as often as it is sent; the unique index on
`(key_id, nonce)` is what makes a signature single-use.

## Outbound requests

Merchant-supplied webhook URLs are a server-side request forgery primitive. Two rules:

- `OutboundAddressPolicy.TryValidateShape` runs when a merchant saves an endpoint, so
  they see the error immediately: https only, ports 443 or 8443, no credentials in the
  URL, no literal private address.
- `OutboundConnect` is the actual boundary. It resolves the host, discards every address
  that is not publicly routable, and connects to a survivor itself, which closes the DNS
  rebinding window that shape validation alone leaves open. Redirects are off, because a
  302 to 127.0.0.1 walks past everything else.

Use the `yopay-outbound` named HttpClient for anything a merchant supplied the address
of. A second client built elsewhere has none of this.

## Things this layer will never do

- Ask for, store or forward a customer's or merchant's MFS PIN or OTP.
- Log a secret, a signature or a full API key.
- Accept a webhook as proof of payment. The SDKs verify server-side as well, because a
  webhook can be forged and an order shipped on a forged webhook is money gone.
