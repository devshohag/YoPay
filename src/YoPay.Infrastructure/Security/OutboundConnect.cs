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
    public static SocketsHttpHandler CreateHandler() => CreateHandler(allowPrivateAddresses: false);

    /// <summary>
    /// <paramref name="allowPrivateAddresses"/> exists for one situation and one only: a
    /// developer running YoPay and their own shop on the same laptop, where the webhook
    /// URL is http://localhost:5000 and every guard in this file is correctly in the way.
    ///
    /// It is off unless Webhooks:AllowPrivateEndpoints is set, the host logs a warning
    /// when it is on, and turning it on in production hands a merchant the ability to make
    /// this server fetch its own cloud metadata endpoint and read the answer back off a
    /// delivery row. The alternative was a developer disabling the guard by hand and
    /// forgetting to put it back, which is how that ends up in production permanently.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(bool allowPrivateAddresses) => new()
    {
        AllowAutoRedirect = false, // a 302 to 127.0.0.1 would otherwise walk straight past this
        ConnectCallback = (context, ct) =>
            ConnectAsync(context.DnsEndPoint, allowPrivateAddresses, ct),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };

    /// <summary>
    /// Resolves the host, discards every address that is not publicly routable, and dials
    /// a survivor. Because the socket opens against an address this method has already
    /// inspected, there is no window in which a second DNS answer redirects the connection
    /// inward.
    /// </summary>
    public static ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, CancellationToken ct) =>
        ConnectAsync(endPoint, allowPrivateAddresses: false, ct);

    public static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endPoint, bool allowPrivateAddresses, CancellationToken ct)
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

        var permitted = allowPrivateAddresses
            ? resolved
            : Array.FindAll(resolved, OutboundAddressPolicy.IsPubliclyRoutable);

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
