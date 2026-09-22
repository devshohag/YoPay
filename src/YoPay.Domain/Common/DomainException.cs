namespace YoPay.Domain.Common;

/// <summary>
/// Thrown when a caller attempts something the domain forbids - an illegal invoice
/// transition, for example. Distinct from infrastructure failures so the API can map
/// it to 409/422 rather than 500.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
