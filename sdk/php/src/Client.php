<?php

declare(strict_types=1);

namespace YoPay;

/**
 * Talks to the YoPay API.
 *
 * Deliberately dependency-free. The shops this is written for run on cPanel hosting where
 * "composer require" is a support ticket, so this needs nothing but curl and json, which
 * every PHP 8 install already has.
 */
final class Client
{
    private string $baseUrl;

    public function __construct(
        private readonly string $keyId,
        private readonly string $secret,
        string $baseUrl = 'https://api.yopay.com.bd',
        private readonly int $timeout = 20
    ) {
        $this->baseUrl = rtrim($baseUrl, '/');
    }

    /**
     * Creates an invoice and returns it, including the checkout URL to send the customer to.
     *
     * Safe to call again with the same orderRef. A repeat answers with the invoice that
     * already exists rather than making a second one, which is what makes a retrying
     * checkout button harmless.
     *
     * @param array<string, mixed> $extra optional: customerName, customerMsisdn,
     *                                    redirectUrl, metadataJson, walletId
     * @return array<string, mixed>
     */
    public function createInvoice(
        string $orderRef,
        float $amount,
        string $method = 'Bkash',
        array $extra = []
    ): array {
        return $this->send('POST', '/v1/payment/create', $extra + [
            'orderRef' => $orderRef,
            'amount' => $amount,
            'method' => $method,
        ]);
    }

    /**
     * Asks the server what actually happened to an invoice.
     *
     * Call this before you ship anything, even when a webhook has already arrived saying
     * the invoice is paid. A webhook is a message from outside; this is the record. The
     * difference is what stands between a shop and an order shipped on a forged POST.
     *
     * @return array<string, mixed>
     */
    public function verify(?string $orderRef = null, ?string $invoiceId = null): array
    {
        return $this->send('POST', '/v1/payment/verify', array_filter([
            'orderRef' => $orderRef,
            'invoiceId' => $invoiceId,
        ], static fn ($v) => $v !== null));
    }

    /** @return array<string, mixed> */
    public function getInvoice(string $invoiceId): array
    {
        return $this->send('GET', '/v1/payment/' . rawurlencode($invoiceId));
    }

    /**
     * Calls off an unpaid invoice and frees the amount it had reserved.
     *
     * Worth doing when a cart is abandoned. Every open invoice holds one amount on the
     * wallet, and a shop that never cancels slowly runs out of usable amounts.
     *
     * @return array<string, mixed>
     */
    public function cancel(?string $orderRef = null, ?string $invoiceId = null): array
    {
        return $this->send('POST', '/v1/payment/cancel', array_filter([
            'orderRef' => $orderRef,
            'invoiceId' => $invoiceId,
        ], static fn ($v) => $v !== null));
    }

    /** @return array<string, mixed> */
    public function registerWebhook(string $url): array
    {
        // The response carries the signing secret. It is the only time anything will:
        // store it before you do anything else with it.
        return $this->send('POST', '/v1/webhooks/endpoints', ['url' => $url]);
    }

    /** @return array<string, mixed> */
    public function listWebhooks(): array
    {
        return $this->send('GET', '/v1/webhooks/endpoints');
    }

    /**
     * @param array<string, mixed>|null $payload
     * @return array<string, mixed>
     */
    private function send(string $method, string $path, ?array $payload = null): array
    {
        $body = $payload === null ? '' : (string) json_encode($payload, JSON_UNESCAPED_SLASHES);
        $timestamp = time();

        // Random per request. The server refuses a nonce it has seen before, so this is
        // what stops a captured request being sent a second time.
        $nonce = bin2hex(random_bytes(16));

        $signature = Signature::signRequest(
            $this->secret, $method, $path, $timestamp, $nonce, $body
        );

        $handle = curl_init($this->baseUrl . $path);

        curl_setopt_array($handle, [
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT => $this->timeout,

            // Not negotiable. The request carries a signature and the response carries
            // payment status; without verification both are readable and editable by
            // whoever is between you and us.
            CURLOPT_SSL_VERIFYPEER => true,
            CURLOPT_SSL_VERIFYHOST => 2,

            CURLOPT_HTTPHEADER => [
                'Content-Type: application/json',
                'X-YoPay-Key: ' . $this->keyId,
                'X-YoPay-Timestamp: ' . $timestamp,
                'X-YoPay-Nonce: ' . $nonce,
                'X-YoPay-Signature: ' . $signature,
            ],
        ]);

        if ($payload !== null) {
            curl_setopt($handle, CURLOPT_POSTFIELDS, $body);
        }

        $response = curl_exec($handle);
        $status = (int) curl_getinfo($handle, CURLINFO_RESPONSE_CODE);
        $error = curl_error($handle);
        curl_close($handle);

        if ($response === false) {
            throw new ApiException('Could not reach YoPay: ' . $error, 0);
        }

        $decoded = json_decode((string) $response, true);

        if ($status >= 400) {
            $title = is_array($decoded) && isset($decoded['title'])
                ? (string) $decoded['title']
                : 'Request failed';

            throw new ApiException($title, $status);
        }

        return is_array($decoded) ? $decoded : [];
    }
}
