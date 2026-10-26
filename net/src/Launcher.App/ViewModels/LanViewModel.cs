using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>One server heard on the network, as shown.</summary>
public sealed record LanServerRow(string Name, string Detail, Uri Coordinator);

/// <summary>The local network: the servers heard on it (join), and joining one by its address. The launcher hosts
/// none: a server is its own program (<c>Online.Server</c>), started by whoever runs one.</summary>
public sealed partial class LanViewModel : ViewModelBase, IDisposable
{
    private readonly AppState _state;
    private readonly Action _showLobby;
    private readonly LanBrowser? _browser;

    public LanViewModel(AppState state, Action showLobby)
    {
        _state = state;
        _showLobby = showLobby;
        try
        {
            _browser = new LanBrowser();
            _browser.Changed += () => OnUi(Refresh);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            Notice = "Not listening for servers on this network";
            NoticeWhole = e.Message;
        }
    }

    public ObservableCollection<LanServerRow> Servers { get; } = [];

    /// <summary>The CPU racers' levels by number, as the lobby and Home name a server's rule.</summary>
    public static IReadOnlyList<string> CpuLevels { get; } = ["Novice", "Standard", "Expert", "Master"];

    [ObservableProperty]
    public partial string Address { get; set; } = "";

    /// <summary>A label ("Could not join: no answer"; "Not listening for servers on this network").</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>What the system said, where it said more than the label (its tooltip); null: no tooltip.</summary>
    [ObservableProperty]
    public partial string? NoticeWhole { get; set; }

    /// <summary>The network is listened to for servers.</summary>
    public bool Listening => _browser is not null;

    /// <summary>The empty list's dot pulses: listening, and the dot shown (no server heard yet). Once one is, the dot
    /// is hidden and its animation stops.</summary>
    public bool ListeningShown => Listening && NoServers;

    /// <summary>No server heard on the network (yet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListeningShown))]
    public partial bool NoServers { get; set; } = true;

    [RelayCommand]
    private Task JoinAsync(LanServerRow server) => JoinUriAsync(server.Coordinator);

    [RelayCommand]
    private Task JoinAddressAsync()
    {
        var a = Address.Trim();
        if (a.Length == 0)
        {
            return Task.CompletedTask;
        }
        var uri = a.Contains("://", StringComparison.Ordinal) ? a : $"ws://{(a.Contains(':', StringComparison.Ordinal) ? a : a + ":47810")}/ws";
        return Uri.TryCreate(uri, UriKind.Absolute, out var u) ? JoinUriAsync(u) : Task.CompletedTask;
    }

    public void Dispose() => _browser?.Dispose();

    private async Task JoinUriAsync(Uri coordinator)
    {
        // RACE's own rule (AppState.WhyNotJoin): no game open, no join on its way, the disc, the game for a race
        if (_state.WhyNotJoin is { } why)
        {
            if (why.Length > 0)
            {
                (Notice, NoticeWhole) = (why, null);   // its own label: no earlier error's tooltip under it
            }
            return;
        }
        try
        {
            var controller = _state.CheckControllerAsync();
            await _state.JoinAsync(coordinator).ConfigureAwait(true);
            _showLobby();
            if (await controller.ConfigureAwait(true) is { } controllerNotice)
            {
                _state.Report(controllerNotice);
            }
        }
        catch (Exception e) when (e is System.Net.WebSockets.WebSocketException or IOException)
        {
            (Notice, NoticeWhole) = ($"Could not join: {AppState.WhyNotReached(e)}", e.Message);
        }
        catch (OperationCanceledException)
        {
            // the player left while it was connecting: nothing to say
        }
    }

    private void Refresh()
    {
        Servers.Clear();
        foreach (var s in _browser?.Servers ?? [])
        {
            Servers.Add(new LanServerRow(s.Name, $"{s.Address}:{s.ControlPort}", s.Coordinator));
        }
        NoServers = Servers.Count == 0;
    }
}
