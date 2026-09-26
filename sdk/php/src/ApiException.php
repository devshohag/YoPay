<?php

declare(strict_types=1);

namespace YoPay;

/**
 * Something the API refused, or could not be asked at all.
 *
 * A status of 0 means the request never got an answer - DNS, TLS, or a timeout. Treat
 * that differently from a 4xx: the call may well have arrived and been acted on, so a
 * blind retry of a create is the one thing that must be safe. It is, because create is
 * idempotent on orderRef.
 */
final class ApiException extends \RuntimeException
{
    public function __construct(string $message, private readonly int $status)
    {
        parent::__construct($message, $status);
    }

    public function status(): int
    {
        return $this->status;
    }

    /** True when nothing answered, so the outcome of the call is genuinely unknown. */
    public function unreachable(): bool
    {
        return $this->status === 0;
    }
}
