using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>
/// The Controller page: the game's four controller ports, each off, a controller through a mapping, or the GameCube
/// adapter; for a controller its device, its mapping ("press to set" for each control, the standard layout) and the
/// sticks' dead zones; and the test, a picture of the GameCube controller that lights what the game sees. What is
/// plugged in, what the player pressed and what the game would read all come from the game's own program started as
/// a helper (<see cref="ControllerHelper"/>) while the page shows and no game runs; the settings are the game's own
/// files (<see cref="ControllerFiles"/>), written at every change (the rest of the class: ControllerViewModel.Helper.cs).
/// </summary>
public sealed partial class ControllerViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly PortSettings[] _ports = [.. Enumerable.Repeat(PortSettings.Off, ControllerFiles.Ports)];
    private IReadOnlyList<ControllerDevice> _devices = [];
    private bool _loading;
    private MapRow? _listening;
    private IReadOnlyDictionary<string, string>? _profile;
    private bool _profileRead;
    private int _captureId;   // the helper's answer to a listening that was ended meanwhile is not this one's

    public ControllerViewModel(AppState state)
    {
        _state = state;
        Rows = [.. PadControls.All.Select(c => new MapRow(c))];
        LeftRows = [.. Rows.Take(Rows.Count / 2)];
        RightRows = [.. Rows.Skip(Rows.Count / 2)];
        state.Changed += () => OnUi(GameChanged);
        state.FocusChanged += () => OnUi(GameChanged);
    }

    public static IReadOnlyList<string> PortNames { get; } = ["Port 1", "Port 2", "Port 3", "Port 4"];

    public static IReadOnlyList<string> KindNames { get; } = ["Off", "Controller", "GameCube adapter"];

    public IReadOnlyList<MapRow> Rows { get; }

    /// <summary>The table's two halves, side by side.</summary>
    public IReadOnlyList<MapRow> LeftRows { get; }

    public IReadOnlyList<MapRow> RightRows { get; }

    public ObservableCollection<DeviceChoice> Devices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PortTitle))]
    public partial int PortIndex { get; set; }

    public string PortTitle => PortNames[PortIndex];

    /// <summary>0 off, 1 a controller, 2 the GameCube adapter; -1: a kind the page does not offer, left as it is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsController), nameof(IsAdapter), nameof(IsOff), nameof(OtherKind))]
    public partial int KindIndex { get; set; }

    public bool IsOff => KindIndex == 0;

    public bool IsController => KindIndex == 1;

    public bool IsAdapter => KindIndex == 2;

    public bool OtherKind => KindIndex < 0;

    [ObservableProperty]
    public partial DeviceChoice? SelectedDevice { get; set; }

    /// <summary>The port's device is not plugged in.</summary>
    [ObservableProperty]
    public partial bool DeviceMissing { get; set; }

    [ObservableProperty]
    public partial bool HasStandardLayout { get; set; }

    /// <summary>What "Use the standard layout" gives for the device chosen.</summary>
    [ObservableProperty]
    public partial string LayoutNote { get; set; } = "";

    [ObservableProperty]
    public partial int StickDeadZone { get; set; }

    [ObservableProperty]
    public partial int CStickDeadZone { get; set; }

    [ObservableProperty]
    public partial bool CanRumble { get; set; }

    /// <summary>The page is waiting for the player to press the input they want.</summary>
    [ObservableProperty]
    public partial bool Capturing { get; set; }

    /// <summary>While listening: the control's label, and the device listened on.</summary>
    [ObservableProperty]
    public partial string CaptureText { get; set; } = "";

    [ObservableProperty]
    public partial string CaptureDevice { get; set; } = "";

    /// <summary>What the game reads from the port now; null: nothing known.</summary>
    [ObservableProperty]
    public partial HelperPad? Pad { get; set; }

    [ObservableProperty]
    public partial string PadNote { get; set; } = "";

    /// <summary>The adapter's state as a label, the value beside it, and its dot: green ready, red a problem, amber
    /// otherwise.</summary>
    [ObservableProperty]
    public partial string AdapterText { get; set; } = "";

    [ObservableProperty]
    public partial string AdapterValue { get; set; } = "";

    [ObservableProperty]
    public partial bool AdapterProblem { get; set; }

    [ObservableProperty]
    public partial bool AdapterReady { get; set; }

    /// <summary>The test's picture is dimmed: the port is off, or its controller is not plugged in.</summary>
    public bool PadDim => Enabled && PadNote.Length > 0;

    partial void OnPadNoteChanged(string value) => OnPropertyChanged(nameof(PadDim));

    partial void OnEnabledChanged(bool value) => ShowPadNote();

    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The ports can be changed: not while the game runs (it reads its settings at its start).</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    /// <summary>Whether the launcher's window is the one in front (the view says; null: nobody can say).</summary>
    public Func<bool>? InFront { get; set; }

    private PortSettings Port
    {
        get => _ports[PortIndex];
        set => _ports[PortIndex] = value;
    }

    private string UserFolder => _state.GamePaths.UserFolder;

    partial void OnPortIndexChanged(int value)
    {
        CancelCapture();
        Pad = null;   // the port before's: this port's comes with the helper's next line
        ShowPort();
        WatchPort();
    }

    partial void OnKindIndexChanged(int value)
    {
        if (_loading || value < 0)
        {
            return;
        }
        var kind = value switch { 1 => PortKind.Controller, 2 => PortKind.Adapter, _ => PortKind.Off };
        var port = Port with { Kind = kind };
        if (kind == PortKind.Controller && port.Device.Length == 0)
        {
            // a port made a controller's for the first time: a gamepad no other port uses, with its standard layout
            var free = _devices.FirstOrDefault(d => d is { Offered: true, Gamepad: true }
                && !_ports.Any(p => p.Kind == PortKind.Controller && p.Device == d.Id));
            port = free is null ? port : StandardLayouts.Applied(port, free, GamepadProfile());
        }
        Port = port;
        Save();
    }

    partial void OnSelectedDeviceChanged(DeviceChoice? value)
    {
        if (_loading || value is null || value.Id == Port.Device)
        {
            return;
        }
        if (value.Device is not { } device)
        {
            return;
        }
        // another controller of the same make keeps the mapping; any other device gets its standard layout, or, with
        // none (a stick, a wheel), an empty table to set by hand
        var same = Port.Device.Length > 0 && ControllerWords.DeviceName(Port.Device) == ControllerWords.DeviceName(device.Id)
            && Port.Device.Split('/')[0] == device.Backend;
        Port = same
            ? Port with { Device = device.Id }
            : StandardLayouts.Applied(Port with { Map = new Dictionary<string, string>() }, device, GamepadProfile());
        Save();
    }

    partial void OnStickDeadZoneChanged(int value) => DeadZonesChanged();

    partial void OnCStickDeadZoneChanged(int value) => DeadZonesChanged();

    private void DeadZonesChanged()
    {
        if (_loading)
        {
            return;
        }
        Port = Port with { StickDeadZone = StickDeadZone, CStickDeadZone = CStickDeadZone };
        Save();
    }

    [RelayCommand]
    private void UseStandardLayout()
    {
        if (SelectedDevice?.Device is { } device && StandardLayouts.For(device) is not null)
        {
            Port = StandardLayouts.Applied(Port, device, GamepadProfile());
            Save();
        }
    }

    /// <summary>"Set" on a row: the helper listens on the port's device for the next input the player moves.</summary>
    [RelayCommand]
    private void Set(MapRow row)
    {
        CancelCapture();
        // only with the launcher's window in front: the helper hears a key wherever it is typed, and one typed into
        // another program is no choice of a control (the view ends a listening when the window is left)
        if (_helper is null || !Enabled || Port.Device.Length == 0 || InFront?.Invoke() == false)
        {
            return;
        }
        _listening = row;
        row.Listening = true;
        Capturing = true;
        CaptureText = row.Label;
        CaptureDevice = ControllerWords.DeviceName(Port.Device);
        _captureId = _helper.Capture(Port.Device, CaptureMs);
    }

    [RelayCommand]
    public void CancelCapture()
    {
        if (_listening is null)
        {
            return;
        }
        _helper?.Capture(null);
        EndCapture();
    }

    [RelayCommand]
    private void Rumble() => _helper?.Rumble(PortIndex, 400);

    private void EndCapture()
    {
        if (_listening is { } row)
        {
            row.Listening = false;
        }
        _listening = null;
        Capturing = false;
        CaptureText = "";
        CaptureDevice = "";
    }

    /// <summary>The helper heard an input (or none in time) for the row that waits.</summary>
    private void Captured(HelperCaptured captured)
    {
        if (_listening is not { } row || captured.Id != _captureId)
        {
            return;
        }
        EndCapture();
        // Esc cancels; Alt and the Windows key leave the window; the mouse sets nothing (ControllerWords.IsChoice)
        if (!ControllerWords.IsChoice(captured) || captured.Expression is not { } expression)
        {
            return;
        }
        var port = Port.With(row.Control.Key, expression);
        Port = row.Control.Also is { } also ? port.With(also, expression) : port;
        Save();
    }

    /// <summary>The settings as the files have them.</summary>
    private void Load()
    {
        var read = ControllerFiles.Read(UserFolder);
        for (var i = 0; i < _ports.Length; i++)
        {
            _ports[i] = read.Ports[i];
        }
        Notice = read.Problem ?? Notice;
        ShowPort();
    }

    /// <summary>The port showing is written, and the helper reads the files again: the test shows what was just set.</summary>
    private void Save()
    {
        CancelCapture();   // a listening for the device or the port as it was is over
        try
        {
            ControllerFiles.Write(UserFolder, PortIndex, Port);
            Notice = "";
        }
        catch (Exception e) when (ControllerFiles.Failed(e))
        {
            Notice = $"Change not saved · {e.Message}";
        }
        ShowPort();
        Reloaded();
    }

    /// <summary>The page's controls set from the port showing.</summary>
    private void ShowPort()
    {
        _loading = true;
        var port = Port;
        KindIndex = port.Kind switch { PortKind.Off => 0, PortKind.Controller => 1, PortKind.Adapter => 2, _ => -1 };
        foreach (var row in Rows)
        {
            row.Input = port.Shown(row.Control.Key);
        }
        StickDeadZone = (int)Math.Round(port.StickDeadZone);
        CStickDeadZone = (int)Math.Round(port.CStickDeadZone);
        ShowDevices();
        _loading = false;
        ShowAdapter();
        ShowPadNote();
    }

    /// <summary>The device list: gamepads, the keyboard, then sticks and wheels; and the port's own device when it is
    /// not plugged in, so that the list never shows another in its place.</summary>
    private void ShowDevices()
    {
        var port = Port;
        var choices = _devices.Where(d => d.Offered || d.Id == port.Device)
            .OrderBy(d => d.Gamepad ? 0 : d.Keyboard ? 1 : 2)
            .Select(d => new DeviceChoice(d.Id, d.Label, d)).ToList();
        var plugged = choices.Any(c => c.Id == port.Device);
        if (!plugged && port.Device.Length > 0)
        {
            choices.Insert(0, new DeviceChoice(port.Device, $"{ControllerWords.DeviceName(port.Device)} (not plugged in)", null));
        }
        if (!Devices.SequenceEqual(choices))
        {
            Devices.Clear();
            foreach (var c in choices)
            {
                Devices.Add(c);
            }
        }
        SelectedDevice = Devices.FirstOrDefault(c => c.Id == port.Device);
        DeviceMissing = port.Kind == PortKind.Controller && port.Device.Length > 0 && !plugged && _helper is not null;
        var device = SelectedDevice?.Device;
        HasStandardLayout = device is not null && StandardLayouts.For(device) is not null;
        LayoutNote = ControllerWords.Layout(port, device, GamepadProfile());
        CanRumble = port.Kind == PortKind.Adapter
            || (port.Kind == PortKind.Controller && device is { Rumble: true } && port.Map.ContainsKey(PadControls.Rumble));
    }

    /// <summary>The runner's own gamepad profile, read once (null: not there; the launcher's copy of it is used).</summary>
    private IReadOnlyDictionary<string, string>? GamepadProfile()
    {
        if (!_profileRead)
        {
            (_profile, _profileRead) = (ControllerFiles.GamepadProfile(_state.GamePaths.Runner), true);
        }
        return _profile;
    }

    // (no game program, which lists and tests the controllers, or a game running: the left column says so, once;
    // the test says nothing of it)
    private void ShowPadNote() => PadNote = !Enabled || _helper is null ? ""
        : IsOff ? "Off"
        : Pad is { Connected: false } && IsAdapter ? (_adapter is { State: AdapterState.Found } ? "No controller in this port" : "")
        : Pad is { Connected: false } ? "Not plugged in"
        : "";
}
