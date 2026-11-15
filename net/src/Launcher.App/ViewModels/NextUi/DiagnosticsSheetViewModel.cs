using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Launcher.Core.Pads;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>A group of the diagnostics: its heading and its facts.</summary>
public sealed record FactGroup(string Heading, IReadOnlyList<SheetFact> Facts);

/// <summary>
/// "Diagnostics" (Developer mode's DEV chip in the top bar): the build (version, commit, stack), the files (the data
/// folder, the log, the replays, the game's folder), the connection (the server, its last answer's time, the lobby and
/// its phase, the input delay), the input (the icons' family, the pad's device and port, the confirm button) and the
/// interface (its scale, reduced motion); "Copy diagnostics", "Open the log", "Open the data folder", "Show the launch
/// file" and "Turn off". Everything is read from the launcher as it is when the panel opens.
/// </summary>
public sealed partial class DiagnosticsSheetViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly ShellViewModel _shell;
    private readonly AppState _state;

    public DiagnosticsSheetViewModel(MainViewModel main, ShellViewModel shell, AppState state)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(state);
        (_main, _shell, _state) = (main, shell, state);
        Groups = Read();
    }

    public IReadOnlyList<FactGroup> Groups { get; }

    /// <summary>What an action could not do ("" nothing).</summary>
    [ObservableProperty]
    public partial string Notice { get; private set; } = "";

    /// <summary>The panel as text (Copy diagnostics: the view puts it on the clipboard).</summary>
    public string Text()
    {
        var text = new StringBuilder();
        foreach (var group in Groups)
        {
            text.Append(group.Heading).AppendLine();
            foreach (var fact in group.Facts)
            {
                text.Append("  ").Append(fact.Name).Append(": ").Append(fact.Value).AppendLine();
            }
        }
        return text.ToString();
    }

    /// <summary>The launcher's log, shown in its folder.</summary>
    [RelayCommand]
    private void OpenLog()
    {
        var log = Path.Combine(_state.DataFolder, Program.LogFileName);
        Notice = File.Exists(log) ? (Replays.TryReveal(log) ? "" : "The log could not be shown")
            : DolphinTools.TryOpenFolder(_state.DataFolder) ? "No log written yet: the data folder" : "The data folder could not be opened";
    }

    [RelayCommand]
    private void OpenDataFolder() => Notice = DolphinTools.TryOpenFolder(_state.DataFolder) ? "" : "The data folder could not be opened";

    /// <summary>The launch file the game read at its last start, shown in its folder.</summary>
    [RelayCommand]
    private void ShowLaunchFile()
    {
        var file = GameLaunchFile.PathFor(_state.GamePaths);
        Notice = !File.Exists(file) ? "No launch file yet: the game has not started" : Replays.TryReveal(file) ? "" : "The launch file could not be shown";
    }

    /// <summary>"Turn off": Developer mode off, the panel closed.</summary>
    [RelayCommand]
    private void TurnOff()
    {
        _main.Settings.DeveloperMode = false;
        _shell.Sheet = null;
    }

    private IReadOnlyList<FactGroup> Read()
    {
        var s = _state.Settings;
        var stack = StackStamp.Current;
        var (family, confirmRight) = _shell.PadLook();
        var port = _state.Pads.NavPortInfo;
        var session = _state.Session;
        var latency = _main.Feed.Latency is { } took ? took.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms" : "–";
        return
        [
            new("Build",
            [
                new("Version", BuildInfo.Text),
                new("Commit", BuildInfo.Build ?? "–"),
                new("Stack", stack.Name is { } name ? $"{name} · {stack.ServerLabel}" : stack.ServerLabel),
            ]),
            new("Files",
            [
                new("Data", _state.DataFolder),
                new("Log", Program.LogFileName),
                new("Replays", s.ReplayFolder ?? Path.Combine(_state.DataFolder, "replays")),
                new("Game", _state.GamePaths.UserFolder),
            ]),
            new("Connection",
            [
                new("Server", s.NoServer ? "none" : s.Server is { Length: > 0 } own ? own : "official"),
                new("Answered in", latency),
                new("Lobby", session?.Lobby is { } lobby ? $"{lobby.LobbyId} · {lobby.Stage}" : session is not null ? "queue" : "none"),
                new("Input delay", string.Create(CultureInfo.InvariantCulture, $"{s.RaceInputDelay} frames")),
            ]),
            new("Input",
            [
                new("Icons", PadIcons.Words(family) + (s.ButtonIcons == ButtonIcons.Auto ? " · auto" : " · chosen")),
                new("Pad", port is null ? "none · keyboard" : port.Device.Length > 0 ? port.Device : "GameCube adapter"),
                new("Port", _state.Pads.NavPort is { } at ? string.Create(CultureInfo.InvariantCulture, $"{at + 1}") : "–"),
                new("Confirm", confirmRight ? "right button" : "bottom button"),
            ]),
            new("Interface",
            [
                new("Scale", string.Create(CultureInfo.InvariantCulture, $"{s.ShownInterfaceScale}%")),
                new("Reduced motion", s.ReducedMotion ? "on" : "off"),
                new("Page", _main.Current?.GetType().Name ?? "–"),
            ]),
        ];
    }
}
