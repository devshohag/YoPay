using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YoPay.Application.Ingestion;

/// <summary>
/// Identifies one observation, so the same message uploaded ten times is stored once.
///
/// Computed on the server from the fields the server itself stores, never taken from the
/// device. A client-supplied hash would let a buggy - or hostile - handset make two
/// different messages collide, or make one message look like ten.
///
/// The device timestamp is part of it on purpose: the same wallet can legitimately receive
/// two identical amounts from the same sender minutes apart, and those are two payments,
/// not one message twice.
/// </summary>
public static class DedupeHash
{
    public static string Compute(
        Guid deviceId, string senderId, string body, DateTimeOffset deviceReceivedAt)
    {
        ArgumentNullException.ThrowIfNull(senderId);
        ArgumentNullException.ThrowIfNull(body);

        var material = string.Join('|',
            deviceId.ToString("N"),
            senderId.Trim().ToUpperInvariant(),
            NormaliseBody(body),
            deviceReceivedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>
    /// The same message arrives from the notification listener and from the SMS receiver
    /// with different whitespace. Collapsing it means the two paths deduplicate against
    /// each other instead of storing the message twice.
    /// </summary>
    private static string NormaliseBody(string body)
    {
        var builder = new StringBuilder(body.Length);
        var lastWasSpace = false;

        foreach (var c in body.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                builder.Append(c);
                lastWasSpace = false;
            }
        }

        return builder.ToString();
    }
}
