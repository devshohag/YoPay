namespace YoPay.Application.Invoicing;

/// <summary>
/// How a payment for this invoice will be recognised.
///
/// TrxId is the default and stays the default through the pilot. It costs a step at
/// checkout - the customer copies a transaction id - and buys something worth more at
/// this stage: a payment can only be matched to the invoice whose id the customer
/// actually pasted, so a wrong match is close to impossible. One wrong match early on
/// costs more trust than a hundred slightly slower checkouts.
///
/// UniqueAmount is the zero-input experience and the one to move to, but only with real
/// numbers in hand: collision rate, abandonment, and how often the fallback fires.
/// </summary>
public enum MatchingMode
{
    TrxId = 0,
    UniqueAmount = 1,
}
