using System.Net;
using System.Net.Sockets;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>A round trip to each relay of a forming match (<see cref="RelayPing"/>; null when none answered).</summary>
public static class RelayMeasure
{
    public static async Task<IReadOnlyList<RelayRtt>> MeasureAsync(
        IReadOnlyList<RelayEndpoint> relays, Func<RelayEndpoint, string> host, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(relays);
        ArgumentNullException.ThrowIfNull(host);
        var results = new List<RelayRtt>();
        foreach (var relay in relays)
        {
            double? rtt = null;
            try
            {
                var address = (await Dns.GetHostAddressesAsync(host(relay), stop).ConfigureAwait(false))
                    .First(a => a.AddressFamily == AddressFamily.InterNetwork);
                using var udp = new UdpClient();
                udp.Connect(new IPEndPoint(address, relay.Port));
                rtt = await RelayPing.MedianMsAsync(udp, stop).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException or InvalidOperationException or ArgumentException)
            {
                // no such host, no IPv4 address of it, or a host that is no address to send to (0.0.0.0, which the
                // name lookup refuses with an ArgumentException): this relay did not answer
            }
            results.Add(new RelayRtt(relay.Id, rtt));
        }
        return results;
    }
}
