using System.Net;
using System.Net.Sockets;
using YoPay.Application.Security;

namespace YoPay.Infrastructure.Security;

/// <summary>
/// The enforcement point. Wire it as SocketsHttpHandler.ConnectCallback and turn
/// redirects off; validating the URL anywhere else is advice, not a boundary.
/// </summary>
public static class OutboundConnect
{
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, // a 302 to 127.0.0.1 would otherwise walk straight past this
        ConnectCallback = static (context, ct) => ConnectAsync(context.DnsEndPoint, ct),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };

    /// <summary>
    /// Resolves the host, discards every address that is not publicly routable, and dials
    /// a survivor. Because the socket opens against an address this method has already
    /// inspected, there is no window in which a second DNS answer redirects the connection
    /// inward.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endPoint);

        IPAddress[] resolved;

        if (IPAddress.TryParse(endPoint.Host, out var literal))
        {
            resolved = [literal];
        }
        else
        {
            try
            {
                resolved = await Dns.GetHostAddressesAsync(endPoint.Host, ct).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                throw new HttpRequestException($"Could not resolve '{endPoint.Host}'.", ex);
            }
        }

        var permitted = Array.FindAll(resolved, OutboundAddressPolicy.IsPubliclyRoutable);

        if (permitted.Length == 0)
        {
            throw new HttpRequestException(
                $"Refusing to connect to '{endPoint.Host}': it resolves only to addresses " +
                "that are not publicly routable.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(permitted, endPoint.Port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
