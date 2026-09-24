namespace YoPay.Application.Matching;

/// <summary>
/// Settles one incoming payment against one invoice, or decides that it cannot.
///
/// The implementation owns a database transaction and a lock, which is why this is a port
/// rather than a service composed of smaller ones: reading the candidates and writing the
/// match have to happen inside the same transaction, or two workers can both read an open
/// session and both decide it is theirs.
/// </summary>
public interface IPaymentMatcher
{
    Task<MatchWorkResult> MatchAsync(IncomingPayment payment, CancellationToken ct = default);
}
