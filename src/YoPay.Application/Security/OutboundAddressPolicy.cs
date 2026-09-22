using System.Net;
using System.Net.Sockets;

namespace YoPay.Application.Security;

/// <summary>
/// Decides whether YoPay may make an outbound request to an address a merchant supplied.
///
/// Ported from the guard in YoMail, which had already worked this out; the reasoning is
/// worth restating because it is the only defence here and the naive version of it does
/// not work.
///
/// A merchant-supplied webhook URL is a server-side request forgery primitive. The
/// merchant registers a host that resolves to the container network, to localhost, or to
/// the cloud metadata service at 169.254.169.254, and YoPay fetches it from inside its
/// own trust boundary. Because the response code and body land on a delivery row the
/// merchant can read back, it is not even blind.
///
/// Two properties matter and only the first is obvious:
///
///   1. The host must not resolve to a private address.
///   2. The address actually connected to must be the one that was checked. Validating a
///      URL and then handing the hostname to HttpClient leaves a DNS rebinding window
///      where the second lookup answers 127.0.0.1. Enforcement therefore belongs in the
///      connect callback, which filters resolved addresses and dials a survivor itself -
///      see OutboundConnect in the infrastructure layer. Redirects must be turned off
///      separately, because a 302 to a private address bypasses all of it.
/// </summary>
public static class OutboundAddressPolicy
{
    /// <summary>No file://, no gopher://, no ftp://, and no plain http.</summary>
    public static bool IsAllowedScheme(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    /// Structural checks that need no DNS. Cheap enough to run when a merchant saves a
    /// webhook, so they get an error in the dashboard instead of silent delivery failures
    /// a week later.
    /// </summary>
    public static bool TryValidateShape(Uri uri, out string? error)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            error = "The URL must be absolute.";
            return false;
        }

        if (!IsAllowedScheme(uri))
        {
            error = "Only https:// endpoints are accepted.";
            return false;
        }

        if (uri.Port is not (443 or 8443))
        {
            error = "Only ports 443 and 8443 are accepted.";
            return false;
        }

        if (uri.UserInfo.Length > 0)
        {
            error = "Credentials in the URL are not accepted.";
            return false;
        }

        // A literal address in the URL never goes near DNS, so it is checked here too.
        if (IPAddress.TryParse(uri.Host, out var literal) && !IsPubliclyRoutable(literal))
        {
            error = "That address is not publicly routable.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// True only for addresses on the public internet. A denylist of non-routable space
    /// rather than an allowlist of known customers, so it keeps working as merchants come
    /// and go.
    /// </summary>
    public static bool IsPubliclyRoutable(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // ::ffff:169.254.169.254 reaches the same host as the bare IPv4 address.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),
            _ => false,
        };
    }

    private static bool IsPublicV4(IPAddress address)
    {
        Span<byte> b = stackalloc byte[4];
        if (!address.TryWriteBytes(b, out _))
        {
            return false;
        }

        return b[0] switch
        {
            0 => false,                                     // 0.0.0.0/8 this network
            10 => false,                                    // 10/8 private
            127 => false,                                   // 127/8 loopback
            >= 224 => false,                                // multicast, reserved, broadcast
            100 when b[1] is >= 64 and <= 127 => false,     // 100.64/10 CGNAT
            169 when b[1] == 254 => false,                  // 169.254/16 link-local: cloud metadata
            172 when b[1] is >= 16 and <= 31 => false,      // 172.16/12 private, the Docker range
            192 when b[1] == 168 => false,                  // 192.168/16 private
            192 when b[1] == 0 && b[2] is 0 or 2 => false,  // IETF protocol assignments, TEST-NET-1
            198 when b[1] is 18 or 19 => false,             // 198.18/15 benchmarking
            198 when b[1] == 51 && b[2] == 100 => false,    // TEST-NET-2
            203 when b[1] == 0 && b[2] == 113 => false,     // TEST-NET-3
            _ => true,
        };
    }

    private static bool IsPublicV6(IPAddress address)
    {
        if (IPAddress.IPv6Any.Equals(address) ||
            IPAddress.IPv6Loopback.Equals(address) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast ||
            address.IsIPv6UniqueLocal)
        {
            return false;
        }

        Span<byte> b = stackalloc byte[16];
        if (!address.TryWriteBytes(b, out _))
        {
            return false;
        }

        // 64:ff9b::/96 NAT64 translates straight back into IPv4 space.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b)
        {
            return false;
        }

        // 2002::/16 6to4 embeds an arbitrary IPv4 address in the prefix.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return false;
        }

        // 2001:db8::/32 documentation and 2001::/32 Teredo.
        if (b[0] == 0x20 && b[1] == 0x01 &&
            ((b[2] == 0x0d && b[3] == 0xb8) || (b[2] == 0x00 && b[3] == 0x00)))
        {
            return false;
        }

        return true;
    }
}
