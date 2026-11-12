using System.Net;
using System.Net.Sockets;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>A round trip to each relay of a forming match (<see cref="RelayPing"/>; null when none answered), all
/// relays at once: one after another, the answer took five round trips to every relay in turn, and with six running
/// it came after the server had chosen the race's relay (2026-11-11, players taken out as too slow).</summary>
public static class RelayMeasure
{
    public static async Task<IReadOnlyList<RelayRtt>> MeasureAsync(
        IReadOnlyList<RelayEndpoint> relays, Func<RelayEndpoint, string> host, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(relays);
        ArgumentNullException.ThrowIfNull(host);
        return await Task.WhenAll(relays.Select(relay => OneAsync(relay, host, stop))).ConfigureAwait(false);   // (in the relays' order)
    }

    private static async Task<RelayRtt> OneAsync(RelayEndpoint relay, Func<RelayEndpoint, string> host, CancellationToken stop)
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
        return new RelayRtt(relay.Id, rtt);
    }
}
