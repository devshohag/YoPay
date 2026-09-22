using YoPay.Domain.Entities;

namespace YoPay.Application.Abstractions;

/// <summary>
/// One way of getting money from a customer to a merchant.
///
/// Only one implementation exists today, but the port is here from day one on purpose.
/// Every rail this product can run on - an approved retail account watched by a device,
/// an ordinary personal number, a licensed gateway, Bangla QR - differs in how a payment
/// is started and confirmed, and in nothing else. Merchants integrate against YoPay, not
/// against a rail, so the day a rail closes or a better one opens, merchant code does not
/// change.
///
/// That is the whole technical insurance policy against a regulatory shift, and it only
/// holds while nothing outside the rail implementations knows which rail is in use.
///
/// Note the callback type: a transport-neutral record, never HttpRequest. A rail that
/// drags ASP.NET into the application layer cannot be unit tested and cannot be driven
/// from a worker.
/// </summary>
public interface IPaymentRail
{
    /// <summary>Stable identifier, e.g. "mfs-pra", "mfs-personal", "bangla-qr".</summary>
    string Code { get; }

    RailCapabilities Capabilities { get; }

    Task<RailSession> OpenAsync(Invoice invoice, CancellationToken ct);

    /// <summary>Asks the rail where a session stands. Device-watched rails answer from
    /// observed events; API-backed rails call the provider.</summary>
    Task<RailStatus> PollAsync(string railSessionId, CancellationToken ct);

    Task<RailResult> HandleCallbackAsync(RailCallback callback, CancellationToken ct);

    /// <summary>Mobile money send-money is irreversible, so those rails return
    /// Unsupported and the merchant refunds out of band with an audit trail.</summary>
    Task<RefundResult> RefundAsync(string paymentReference, decimal amount, CancellationToken ct);
}
