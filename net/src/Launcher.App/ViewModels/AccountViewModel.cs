using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// The left column's two ends: at the top the player (the name, and the machine last raced, by name); at the bottom
/// a dot for whether the online server answers, which server, and the launcher's version. The name opens a menu to
/// change it (<c>Views/NameMenu</c>); the machine opens the Garage.
/// </summary>
public sealed partial class AccountViewModel : ViewModelBase, IDisposable
{
    private readonly AppState _state;
    private readonly ServerHealth _health;
    private readonly Action _showGarage;
    private readonly StackStamp _stack;
    private QuickPlaySession? _watched;

    /// <param name="shown">Whether the left column shows (the first screen is passed): until then the dot asks
    /// nobody (<see cref="ServerChoice.AskedUnprompted"/>).</param>
    /// <param name="stack">The server stack the build was made for (this build's own unless a test names another).</param>
    public AccountViewModel(AppState state, Action showGarage, Func<bool> shown, StackStamp? stack = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(shown);
        _state = state;
        _showGarage = showGarage;
        _stack = stack ?? StackStamp.Current;
        _health = new ServerHealth(() => ServerChoice.AskedUnprompted(state.Settings, shown()), () => state.Session?.ConnectedTo,
            state.ServiceAnswers());
        _health.Changed += () => OnUi(RefreshConnection);
        state.Changed += () => OnUi(Refresh);
        Refresh();
        _health.Start();
    }

    /// <summary>The name as the game shows it.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>The player's tag, shown after the name ("NAME #4F2A"): this connection's while in Quick Play (a pair
    /// taken by another player gets one for the connection), else the saved one; null before the first.</summary>
    [ObservableProperty]
    public partial string? Tag { get; set; }

    /// <summary>The machine last raced, by name.</summary>
    [ObservableProperty]
    public partial string Machine { get; set; } = "";

    /// <summary>Which server the dot is about: "official server" (with its stack's name on a build made for one,
    /// "test server" on a test build), or the host of the player's own; blank when no
    /// server is set (the dot's word says so).</summary>
    [ObservableProperty]
    public partial string ServerText { get; set; } = "";

    /// <summary>The dot's state for the view's colours: "online", "waiting" (checking, or slow) or "offline".</summary>
    [ObservableProperty]
    public partial string Connection { get; set; } = "waiting";

    /// <summary>The dot's word.</summary>
    [ObservableProperty]
    public partial string ConnectionText { get; set; } = "Checking";

    /// <summary>The dot's tooltip: which server, what it said, when.</summary>
    [ObservableProperty]
    public partial string ConnectionDetail { get; set; } = "";

    public static string Version => BuildInfo.Text;

    /// <summary>The mark of a test build under the version (<see cref="StackStamp.Mark"/>); blank on any other build,
    /// whose box has no such line (<see cref="HasBuildMark"/>).</summary>
    public string BuildMark => _stack.Mark ?? "";

    public bool HasBuildMark => _stack.Mark is not null;

    /// <summary>The name being typed in the menu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DraftShown), nameof(DraftLeft), nameof(DraftLost), nameof(HasLost), nameof(CanSave))]
    public partial string Draft { get; set; } = "";

    public string DraftShown => PlayerNames.Shown(Draft) is { Length: > 0 } n ? n : "[YOUR NAME]";

    /// <summary>Under the box: the room left ("5 left").</summary>
    public string DraftLeft => $"{PlayerNames.MaxLength - PlayerNames.Shown(Draft).Length} left";

    /// <summary>The characters typed that the game cannot draw, spaced ("Ü É"): left out of the name, and a name of
    /// nothing else cannot be saved. Blank when there are none.</summary>
    public string DraftLost => string.Join(' ', Draft.Where(c => !PlayerNames.IsAllowed(c) && !char.IsControl(c) && !char.IsWhiteSpace(c)).Distinct().Take(8));

    /// <summary>The box holds characters the game cannot draw: "Not shown" and which, in place of the room left.</summary>
    public bool HasLost => DraftLost.Length > 0;

    /// <summary>Whether the box holds a name: something of it is left as the game shows it.</summary>
    public bool CanSave => PlayerNames.Shown(Draft).Length > 0;

    /// <summary>The menu opened: start from the saved name.</summary>
    public void EditName() => Draft = _state.Settings.Racer.Name;

    /// <summary>Keeps the typed name, as the game shows it. False: the box holds no name, and nothing changed.</summary>
    public bool SaveName()
    {
        var shown = PlayerNames.Shown(Draft);
        if (shown.Length == 0)
        {
            return false;
        }
        if (shown != _state.Settings.Racer.Name)
        {
            _state.Rename(shown);
        }
        return true;
    }

    [RelayCommand]
    private void OpenGarage() => _showGarage();

    private RacedMachine? _shown;

    /// <summary>The machine the left column shows (the window says: the one selected, or the one the server races
    /// the player with): its name stands under the player's, a custom machine's by the game's name for it.</summary>
    public void ShowMachine(RacedMachine raced)
    {
        ArgumentNullException.ThrowIfNull(raced);
        if (Machines.Has(raced.Machine) && !raced.Same(_shown))
        {
            _shown = raced;
            Machine = MachineText(raced);
        }
    }

    /// <summary>A machine's name for the column (<see cref="Theme.GameSkin.NameOf"/>).</summary>
    private string MachineText(RacedMachine raced) => Theme.GameSkin.NameOf(raced, () => OnUi(Refresh));

    /// <summary>The first screen is passed: the server is asked now, not at the next settings change.</summary>
    public void Shown() => Refresh();

    private void Refresh()
    {
        var racer = _state.Settings.Racer;
        Name = PlayerNames.Shown(racer.Name);
        RefreshTag();
        Machine = MachineText(_shown ?? racer.Raced with { Machine = Math.Clamp(racer.Machine, 0, Machines.Names.Count - 1) });
        ServerText = _state.Settings.ServerOn(_stack) is not { } server ? ""
            : string.IsNullOrWhiteSpace(_state.Settings.Server) ? _stack.ServerLabel : ServerHealth.HostOf(server);
        if (_state.Session != _watched)
        {
            _watched = _state.Session;
            if (_watched is { } session)
            {
                // the session connecting or ending changes the dot (in Quick Play counts as online)
                session.Changed += () => OnUi(() =>
                {
                    if (session == _state.Session)
                    {
                        _health.Refresh();
                        RefreshTag();
                    }
                });
            }
        }
        _health.Refresh();   // the server setting may have changed
    }

    /// <summary>The tag is the server's, worked out from the launcher's secret and its own key: known only while
    /// connected, so out of Quick Play the name stands alone.</summary>
    private void RefreshTag() =>
        Tag = _state.InQuickPlay && _state.Session?.Tag is { } now ? now : null;

    private void RefreshConnection()
    {
        var state = _health.State;
        Connection = state switch
        {
            Connectivity.Online => "online",
            Connectivity.Offline or Connectivity.NoServer => "offline",
            _ => "waiting",
        };
        ConnectionText = state switch
        {
            Connectivity.Online => "Online",
            Connectivity.Slow => "Online, slow",
            Connectivity.Offline => "Offline",
            Connectivity.NoServer => "No server set",
            _ => "Checking",
        };
        if (_health.Server is not { } asked)
        {
            // nothing is asked of anyone until the player chooses a server
            ConnectionDetail = "No server";
            return;
        }
        var server = (string.IsNullOrWhiteSpace(_state.Settings.Server) ? "Official server: " : "Server: ") +
            ServerHealth.HostOf(asked);
        var said = _health.InQuickPlay ? "In Quick Play"
            : state == Connectivity.Checking ? "Checking"
            : _health.LastAnswer is { } took ? $"{took.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms"
            : "No answer";
        var when = _health.LastCheck is { } at ? $"\nChecked {at.ToLocalTime():HH:mm:ss}" : "";
        ConnectionDetail = $"{server}\n{said}{when}";
    }

    public void Dispose() => _health.Dispose();
}
