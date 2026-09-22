namespace YoPay.Domain.Invoicing;

/// <summary>
/// How close an incoming amount has to be before it counts as payment in full.
///
/// Exact by default. A merchant may allow a small shortfall - customers round down, and
/// chasing five taka costs more than it saves - but anything below the floor is a
/// partial payment and stays visible rather than being quietly accepted.
/// </summary>
public readonly record struct AmountTolerance(decimal AbsoluteBdt)
{
    public static readonly AmountTolerance Exact = new(0m);

    public bool IsFullPayment(decimal expected, decimal received) =>
        received >= expected - AbsoluteBdt;

    public bool IsOverpayment(decimal expected, decimal received) =>
        received > expected + AbsoluteBdt;
}
