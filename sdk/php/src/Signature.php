<?php

declare(strict_types=1);

namespace YoPay;

/**
 * The two signatures in this system, in one place.
 *
 * They are deliberately different shapes because they solve different problems, and
 * conflating them is the easiest way to get either one wrong:
 *
 * - A REQUEST signature proves that a call to YoPay came from this merchant. It covers
 *   the method and the path as well as the body, because without them a signed GET
 *   replays as a DELETE and a signature for one invoice authorises another. It carries a
 *   nonce, because the server refuses a nonce it has already seen and that is what stops
 *   a captured request being sent twice.
 *
 * - A WEBHOOK signature proves that a delivery came from YoPay. There is no nonce: the
 *   merchant's server has no shared store to remember one in, so the defence against
 *   replay is the timestamp inside the MAC plus the merchant's own idempotency.
 *
 * Hex case is not significant on either side. The server normalises before comparing.
 */
final class Signature
{
    /** How far the clocks may be apart, in seconds. */
    public const TOLERANCE = 300;

    /**
     * The canonical string a request signature covers.
     *
     * The body is hashed rather than concatenated so the shape stays fixed and readable
     * whatever the body is - and so that reproducing it in another language is mechanical
     * rather than a matter of interpretation.
     */
    public static function canonicalRequest(
        string $method,
        string $pathAndQuery,
        int $timestamp,
        string $nonce,
        string $body
    ): string {
        return implode("\n", [
            strtoupper($method),
            $pathAndQuery,
            (string) $timestamp,
            $nonce,
            strtoupper(hash('sha256', $body)),
        ]);
    }

    public static function signRequest(
        string $secret,
        string $method,
        string $pathAndQuery,
        int $timestamp,
        string $nonce,
        string $body
    ): string {
        return hash_hmac(
            'sha256',
            self::canonicalRequest($method, $pathAndQuery, $timestamp, $nonce, $body),
            $secret
        );
    }

    /**
     * Checks the X-YoPay-Signature header on a delivery YoPay sent you.
     *
     * Pass the RAW request body, exactly as it arrived. Decoding the JSON and encoding it
     * again changes the whitespace, and the MAC is over bytes - this is the single most
     * common reason a correct integration reports that "signatures never verify".
     */
    public static function verifyWebhook(
        string $secret,
        string $header,
        string $body,
        ?int $now = null
    ): bool {
        if ($secret === '' || $header === '') {
            return false;
        }

        $parts = [];

        foreach (explode(',', $header) as $piece) {
            $pair = explode('=', trim($piece), 2);

            if (count($pair) === 2) {
                $parts[$pair[0]] = $pair[1];
            }
        }

        if (!isset($parts['t'], $parts['v1']) || !ctype_digit($parts['t'])) {
            return false;
        }

        $now ??= time();

        // Checked before the MAC. Skip this and the signature never expires, which turns
        // any captured "paid" delivery into a free order for whoever recorded it.
        if (abs($now - (int) $parts['t']) > self::TOLERANCE) {
            return false;
        }

        $expected = hash_hmac('sha256', $parts['t'] . '.' . $body, $secret);

        // Constant time. A normal comparison leaks how much of a forged MAC was right,
        // which is enough to build the rest of it one byte at a time.
        return hash_equals($expected, strtolower($parts['v1']));
    }
}
