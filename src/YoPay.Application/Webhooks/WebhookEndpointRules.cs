using System.Security.Cryptography;
using YoPay.Application.Security;

namespace YoPay.Application.Webhooks;

public sealed record EndpointCheck
{
    public required bool Accepted { get; init; }
    public string? Error { get; init; }
    public Uri? Url { get; init; }
}

/// <summary>
/// Whether a merchant's webhook URL may be saved at all, and what secret it gets.
///
/// The structural check runs when the merchant types the URL rather than only when the
/// first payment arrives. A merchant who pastes an http:// address should be told so
/// while they are looking at the form, not discover it a week later as five dead-lettered
/// deliveries - and a rejected URL never reaches the dispatcher in the first place.
///
/// Passing here is not the end of it. The address a hostname resolves to can change
/// between this check and the request, so the connect callback checks again at dial time.
/// This is the fast, friendly half of a defence whose serious half lives in the socket.
/// </summary>
public static class WebhookEndpointRules
{
    /// <summary>
    /// <paramref name="allowPrivate"/> loosens two rules - http:// and a private address -
    /// for a developer running YoPay and their shop on one laptop. It is off unless
    /// Webhooks:AllowPrivateEndpoints says otherwise, and it must never be on in
    /// production: a merchant could then point an endpoint at this server's own metadata
    /// service and read the answer off a delivery row.
    /// </summary>
    public static EndpointCheck Check(string? url, bool allowPrivate = false)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new EndpointCheck { Accepted = false, Error = "Enter a URL." };
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return new EndpointCheck { Accepted = false, Error = "That is not a valid URL." };
        }

        if (allowPrivate)
        {
            // Still not anything: a scheme that is not http or https, or credentials in
            // the URL, are mistakes at any time and on any machine.
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            {
                return new EndpointCheck
                {
                    Accepted = false,
                    Error = "Only http:// and https:// endpoints are accepted.",
                };
            }

            if (uri.UserInfo.Length > 0)
            {
                return new EndpointCheck
                {
                    Accepted = false,
                    Error = "Credentials in the URL are not accepted.",
                };
            }

            return new EndpointCheck { Accepted = true, Url = uri };
        }

        if (!OutboundAddressPolicy.TryValidateShape(uri, out var error))
        {
            return new EndpointCheck { Accepted = false, Error = error };
        }

        return new EndpointCheck { Accepted = true, Url = uri };
    }

    /// <summary>
    /// A fresh signing secret. 32 bytes because the MAC is SHA-256 and a key shorter than
    /// the digest is the weakest part of the construction; base64 because it has to
    /// survive being copied out of a dashboard and pasted into a PHP config file.
    /// </summary>
    public static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
