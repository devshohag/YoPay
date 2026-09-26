using System;

namespace YoPay.Sdk;

/// <summary>
/// Something the API refused, or could not be asked at all.
/// </summary>
public sealed class YoPayException : Exception
{
    public YoPayException(string message, int status, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
    }

    /// <summary>The HTTP status, or 0 when nothing answered.</summary>
    public int Status { get; }

    /// <summary>
    /// True when the request never got an answer, so you do not know whether it worked.
    ///
    /// Worth branching on. A 400 means try something different; this means try the same
    /// thing again - which is safe, because create and cancel are both idempotent on the
    /// order reference.
    /// </summary>
    public bool Unreachable => Status == 0;
}
