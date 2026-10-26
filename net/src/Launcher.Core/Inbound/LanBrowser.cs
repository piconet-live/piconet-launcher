using System.Net;
using System.Net.Sockets;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>A server heard on the local network.</summary>
public sealed record LanServer(string Name, IPAddress Address, int ControlPort, DateTimeOffset LastHeard)
{
    public Uri Coordinator => new($"ws://{Address}:{ControlPort}{ControlProtocol.Path}");
}

/// <summary>
/// Lists the servers on the local network: servers broadcast a <see cref="LanAnnouncement"/> every couple of seconds;
/// one not heard for <see cref="Expiry"/> drops off. <see cref="Changed"/> fires on the listening
/// thread.
/// </summary>
public sealed class LanBrowser : IDisposable
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<(IPAddress, int), LanServer> _servers = [];
    private readonly TimeProvider _time;

    public LanBrowser(TimeProvider? time = null, int port = LanAnnouncement.Port)
    {
        _time = time ?? TimeProvider.System;
        _udp = new UdpClient();
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        _ = Task.Run(() => ListenAsync(_stop.Token));
    }

    public static TimeSpan Expiry { get; } = TimeSpan.FromSeconds(8);

    public event Action? Changed;

    public IReadOnlyList<LanServer> Servers
    {
        get
        {
            lock (_servers)
            {
                var now = _time.GetUtcNow();
                return [.. _servers.Values.Where(s => now - s.LastHeard < Expiry).OrderBy(s => s.Name, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>Servers listed at most: any computer on the network can announce any number of them.</summary>
    public const int MaxServers = 64;

    /// <summary>Records one announcement (tests feed these directly). Anyone on the network can send one, so it is
    /// taken only with a port a server can listen on (the list builds an address from it), its name is shown as
    /// plain text of a server name's length, and the list holds <see cref="MaxServers"/> at most: servers not heard
    /// for <see cref="Expiry"/> leave it, and when it is full a new one is not added.</summary>
    public void Heard(LanAnnouncement announcement, IPAddress from)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        if (announcement.ControlPort is < 1 or > 65535)
        {
            return;
        }
        lock (_servers)
        {
            var now = _time.GetUtcNow();
            foreach (var gone in _servers.Where(s => now - s.Value.LastHeard >= Expiry).Select(s => s.Key).ToList())
            {
                _servers.Remove(gone);
            }
            var key = (from, announcement.ControlPort);
            if (_servers.Count >= MaxServers && !_servers.ContainsKey(key))
            {
                return;
            }
            _servers[key] = new LanServer(ServerMessages.Text(announcement.Name, 40), from, announcement.ControlPort, now);
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _udp.Dispose();
        _stop.Dispose();
    }

    private async Task ListenAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var r = await _udp.ReceiveAsync(stop).ConfigureAwait(false);
                if (LanAnnouncement.Decode(r.Buffer) is { } a && a.Version == ControlProtocol.Version)
                {
                    Heard(a, r.RemoteEndPoint.Address);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
            }
        }
    }
}
