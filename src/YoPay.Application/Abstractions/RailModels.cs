using YoPay.Domain.Enums;

namespace YoPay.Application.Abstractions;

/// <summary>
/// What a rail can and cannot do. Read before offering a merchant a feature rather than
/// assumed, so an unsupported refund surfaces at checkout instead of at the moment a
/// customer asks for their money back.
/// </summary>
public sealed record RailCapabilities
{
    public required bool SupportsRefund { get; init; }
    public required bool SupportsRecurring { get; init; }
    public required bool SupportsCards { get; init; }

    /// <summary>True when the provider itself confirms the payment. False when
    /// confirmation comes from a device watching messages, which is slower and is why
    /// the grace window exists.</summary>
    public required bool HasProviderConfirmation { get; init; }

    public required TimeSpan ExpectedConfirmationDelay { get; init; }
}

public sealed record RailSession
{
    public required string RailSessionId { get; init; }
    public required decimal ChargedAmount { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string? PayToNumber { get; init; }
    public string? RedirectUrl { get; init; }
    public PaymentMethod Method { get; init; }
}

public enum RailPaymentState
{
    Pending = 0,
    Confirmed = 1,
    Underpaid = 2,
    Expired = 3,
    Failed = 4,
}

public sealed record RailStatus
{
    public required RailPaymentState State { get; init; }
    public decimal? ReceivedAmount { get; init; }
    public string? ProviderReference { get; init; }

    /// <summary>When the payment happened according to the rail - never server time.</summary>
    public DateTimeOffset? OccurredAt { get; init; }
}

/// <summary>Transport-neutral callback. Built by whichever host received it.</summary>
public sealed record RailCallback
{
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public required string Body { get; init; }
    public required string RemoteIp { get; init; }
}

public sealed record RailResult
{
    public required bool Handled { get; init; }
    public string? RailSessionId { get; init; }
    public RailStatus? Status { get; init; }
    public string? Reason { get; init; }

    public static RailResult Unsupported(string reason) =>
        new() { Handled = false, Reason = reason };
}

public sealed record RefundResult
{
    public required bool Succeeded { get; init; }
    public string? ProviderReference { get; init; }
    public string? Reason { get; init; }

    public static RefundResult Unsupported(string reason) =>
        new() { Succeeded = false, Reason = reason };
}
