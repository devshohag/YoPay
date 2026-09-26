<?php

/**
 * Checks this SDK against signatures the server itself produced.
 *
 * vectors.json is generated from the C# implementation, not written by hand. Two
 * independent descriptions of a MAC always drift eventually, and the failure is silent:
 * every call returns 401 and nobody can tell which side is wrong. Run this after changing
 * anything in Signature.php.
 *
 *   php tests/signature_test.php
 */

declare(strict_types=1);
require __DIR__ . '/../src/autoload.php';

$data = json_decode((string) file_get_contents(__DIR__ . '/vectors.json'), true);
$secret = $data['secret'];
$pass = 0; $fail = 0;

foreach ($data['cases'] as $i => $c) {
    if ($c['kind'] === 'request') {
        $canonical = YoPay\Signature::canonicalRequest(
            $c['method'], $c['path'], $c['ts'], $c['nonce'], $c['body']);

        if ($canonical !== $c['canonical']) {
            $fail++;
            echo "FAIL canonical #$i\n  php: " . str_replace("\n", '\n', $canonical)
               . "\n  net: " . str_replace("\n", '\n', $c['canonical']) . "\n";
            continue;
        }
        $pass++;

        $sig = YoPay\Signature::signRequest(
            $secret, $c['method'], $c['path'], $c['ts'], $c['nonce'], $c['body']);

        if (strcasecmp($sig, $c['signature']) !== 0) {
            $fail++;
            echo "FAIL request signature #$i\n  php: $sig\n  net: {$c['signature']}\n";
        } else {
            $pass++;
        }
    } else {
        // The header C# produced must verify with the PHP verifier.
        if (YoPay\Signature::verifyWebhook($secret, $c['header'], $c['body'], $c['ts'])) {
            $pass++;
        } else {
            $fail++;
            echo "FAIL webhook verify #$i  header={$c['header']}\n";
        }

        // And the things that must NOT verify.
        $checks = [
            'edited body'   => YoPay\Signature::verifyWebhook($secret, $c['header'], $c['body'] . ' ', $c['ts']),
            'wrong secret'  => YoPay\Signature::verifyWebhook('nope', $c['header'], $c['body'], $c['ts']),
            'week-old'      => YoPay\Signature::verifyWebhook($secret, $c['header'], $c['body'], $c['ts'] + 604800),
            // Only the hex value is uppercased - the case an SDK in another language
            // would actually produce. Uppercasing the whole header would break the
            // parameter names, which tests nothing.
            'uppercase mac still verifies' => !YoPay\Signature::verifyWebhook(
                $secret,
                preg_replace_callback('/v1=([0-9a-f]+)/', static fn ($m) => 'v1=' . strtoupper($m[1]), $c['header']),
                $c['body'], $c['ts']),
        ];
        foreach ($checks as $name => $bad) {
            if ($bad) { $fail++; echo "FAIL $name should not verify (#$i)\n"; } else { $pass++; }
        }
    }
}

echo "\nphp pass=$pass fail=$fail\n";
exit($fail === 0 ? 0 : 1);
