namespace Launcher.Core;

/// <summary>What is plugged into one of the game's four controller ports.</summary>
public enum PortKind
{
    /// <summary>Nothing.</summary>
    Off,

    /// <summary>A controller or the keyboard, through a mapping.</summary>
    Controller,

    /// <summary>A GameCube controller through the GameCube adapter, read directly: no mapping.</summary>
    Adapter,

    /// <summary>A kind the page does not offer (a Game Boy Advance, bongos): the settings file's own, left as it is.</summary>
    Other,
}

/// <summary>One of the game's controller ports as the game's settings have it (<c>Config\Dolphin.ini</c>,
/// <c>[Core] SIDeviceN</c>, and <c>Config\GCPadNew.ini</c>, <c>[GCPadN]</c>).</summary>
/// <param name="Kind">What is plugged in.</param>
/// <param name="Device">The device the mapping is for, as the game names it (<c>SDL/0/Xbox One Controller</c>); "":
/// none named.</param>
/// <param name="Map">Each mapped GameCube control (<see cref="PadControls"/>: its key in the file) with the input
/// it is set to, as the file writes it (<c>`Button A`</c>).</param>
/// <param name="StickDeadZone">How far the stick moves before the game sees it, in percent.</param>
/// <param name="CStickDeadZone">The same for the C-Stick.</param>
/// <param name="RawKind">The file's own number for <see cref="Kind"/> (<see cref="PortKind.Other"/> keeps it).</param>
public sealed record PortSettings(PortKind Kind, string Device, IReadOnlyDictionary<string, string> Map,
    double StickDeadZone = 0, double CStickDeadZone = 0, int RawKind = -1)
{
    /// <summary>The file's numbers for the kinds the page offers.</summary>
    public const int NothingNumber = 0, ControllerNumber = 6, AdapterNumber = 12;

    public static PortSettings Off { get; } = new(PortKind.Off, "", new Dictionary<string, string>());

    /// <summary>The file's number of this port's kind.</summary>
    public int Number => Kind switch
    {
        PortKind.Off => NothingNumber,
        PortKind.Controller => ControllerNumber,
        PortKind.Adapter => AdapterNumber,
        _ => RawKind,
    };

    public static PortKind KindOf(int number) => number switch
    {
        NothingNumber => PortKind.Off,
        ControllerNumber => PortKind.Controller,
        AdapterNumber => PortKind.Adapter,
        _ => PortKind.Other,
    };

    /// <summary>The input a control is set to, as a player reads it (the file's quotes taken off); "": not set.</summary>
    public string Shown(string key) => Map.TryGetValue(key, out var value) ? PadControls.Plain(value) : "";

    public PortSettings With(string key, string expression)
    {
        var map = new Dictionary<string, string>(Map, StringComparer.Ordinal) { [key] = expression };
        return this with { Map = map };
    }
}

/// <summary>One control of a GameCube controller in the page's mapping table.</summary>
/// <param name="Key">Its key in the game's controller settings (<c>Buttons/A</c>).</param>
/// <param name="Label">What the page calls it.</param>
/// <param name="Also">A second key set to the same input: a trigger's "how far" beside its "pressed".</param>
public sealed record PadControl(string Key, string Label, string? Also = null);

/// <summary>The GameCube controller's controls, in the order the page lists them.</summary>
public static class PadControls
{
    public static IReadOnlyList<PadControl> All { get; } =
    [
        new("Buttons/A", "A"), new("Buttons/B", "B"), new("Buttons/X", "X"), new("Buttons/Y", "Y"),
        new("Buttons/Z", "Z"), new("Buttons/Start", "Start"),
        new("Triggers/L", "L", "Triggers/L-Analog"), new("Triggers/R", "R", "Triggers/R-Analog"),
        new("Main Stick/Up", "Stick up"), new("Main Stick/Down", "Stick down"),
        new("Main Stick/Left", "Stick left"), new("Main Stick/Right", "Stick right"),
        new("C-Stick/Up", "C-Stick up"), new("C-Stick/Down", "C-Stick down"),
        new("C-Stick/Left", "C-Stick left"), new("C-Stick/Right", "C-Stick right"),
        new("D-Pad/Up", "D-Pad up"), new("D-Pad/Down", "D-Pad down"),
        new("D-Pad/Left", "D-Pad left"), new("D-Pad/Right", "D-Pad right"),
    ];

    public const string Rumble = "Rumble/Motor", StickDeadZone = "Main Stick/Dead Zone", CStickDeadZone = "C-Stick/Dead Zone";

    /// <summary>Every key of a port's section that a standard layout owns: set by it, or taken out by it. A key
    /// that is not here (one the page does not know) is never touched.</summary>
    public static IReadOnlyList<string> LayoutKeys { get; } =
    [
        .. All.Select(c => c.Key), .. All.Where(c => c.Also is not null).Select(c => c.Also!), Rumble,
        "Main Stick/Modifier", "C-Stick/Modifier", "Main Stick/Calibration", "C-Stick/Calibration",
    ];

    /// <summary>An input as a player reads it: one quoted name without its quotes, anything longer as it is.</summary>
    public static string Plain(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var t = expression.Trim();
        return t.Length > 2 && t[0] == '`' && t[^1] == '`' && t.IndexOf('`', 1) == t.Length - 1 ? t[1..^1] : t;
    }
}

/// <summary>A device the game's input layer found, in the helper's words (<see cref="ControllerHelper"/>).</summary>
/// <param name="Id">The name the game's settings know it by: <c>BACKEND/INDEX/NAME</c>.</param>
/// <param name="Backend">What reads it: SDL, DInput, XInput, WGInput.</param>
/// <param name="Name">Its own name.</param>
/// <param name="Gamepad">SDL knows its layout: the standard layout fits it with no mapping by hand.</param>
/// <param name="Rumble">It has a motor.</param>
public sealed record ControllerDevice(string Id, string Backend, int Index, string Name, bool Gamepad, bool Rumble)
{
    /// <summary>The keyboard (with the mouse): one device to the game.</summary>
    public bool Keyboard => Name.Equals("Keyboard Mouse", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name a player knows it by; what reads it is said only where two devices would read alike.</summary>
    public string Label => Keyboard ? "Keyboard" : Gamepad ? Name : $"{Name} ({(Backend == "DInput" ? "DirectInput" : Backend)})";

    /// <summary>A GameCube controller on the GameCube adapter, as SDL shows it while no port is set to the adapter.
    /// SDL lays its buttons out by where they sit (its B is the one to the right, the GameCube's X), so the standard
    /// layout would swap B and X: such a controller is used through the adapter itself (<see cref="PortKind.Adapter"/>),
    /// which needs no mapping.</summary>
    public bool OnAdapter => Backend == "SDL" && Name.Equals("Nintendo GameCube Controller", StringComparison.OrdinalIgnoreCase);

    /// <summary>Offered on the page: SDL's devices and DirectInput's. The others (XInput, Windows.Gaming.Input) are
    /// the same controllers a second time, under names a player does not know; the adapter's are offered as the
    /// adapter.</summary>
    public bool Offered => Backend is "SDL" or "DInput" && !OnAdapter;
}

/// <summary>The layouts "Use the standard layout" sets, and the first start's choice of a controller.</summary>
public static class StandardLayouts
{
    /// <summary>The keyboard's device name on Windows, where the game runs.</summary>
    public const string KeyboardDevice = "DInput/0/Keyboard Mouse";

    /// <summary>The keyboard: the emulator's own default (its GCPad::LoadDefaults, Windows key names). Arrows steer;
    /// X accelerates (A), Z brakes (B), C and S are X and Y, D is Z, Enter is Start, Q and W are L and R; the C-Stick
    /// is I K J L, the D-Pad T G F H; Shift and Ctrl halve the sticks. The sticks' range is a square, so two arrows
    /// together reach a corner.</summary>
    public static IReadOnlyDictionary<string, string> Keyboard { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Buttons/A"] = "`X`", ["Buttons/B"] = "`Z`", ["Buttons/X"] = "`C`", ["Buttons/Y"] = "`S`",
        ["Buttons/Z"] = "`D`", ["Buttons/Start"] = "`RETURN`",
        ["Main Stick/Up"] = "`UP`", ["Main Stick/Down"] = "`DOWN`", ["Main Stick/Left"] = "`LEFT`", ["Main Stick/Right"] = "`RIGHT`",
        ["Main Stick/Modifier"] = "`Shift`", ["Main Stick/Calibration"] = SquareRange,
        ["C-Stick/Up"] = "`I`", ["C-Stick/Down"] = "`K`", ["C-Stick/Left"] = "`J`", ["C-Stick/Right"] = "`L`",
        ["C-Stick/Modifier"] = "`Ctrl`", ["C-Stick/Calibration"] = SquareRange,
        ["Triggers/L"] = "`Q`", ["Triggers/R"] = "`W`",
        ["D-Pad/Up"] = "`T`", ["D-Pad/Down"] = "`G`", ["D-Pad/Left"] = "`F`", ["D-Pad/Right"] = "`H`",
    };

    /// <summary>A gamepad SDL knows, when the runner's own profile (<c>Sys\Profiles\GCPad\SDL Gamepad.ini</c>) is not
    /// there to read: the same lines.</summary>
    public static IReadOnlyDictionary<string, string> Gamepad { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Buttons/A"] = "`Button A`", ["Buttons/B"] = "`Button B`", ["Buttons/X"] = "`Button X`", ["Buttons/Y"] = "`Button Y`",
        ["Buttons/Z"] = "`Shoulder R`", ["Buttons/Start"] = "`Start`",
        ["Main Stick/Up"] = "`Left Y+`", ["Main Stick/Down"] = "`Left Y-`", ["Main Stick/Left"] = "`Left X-`", ["Main Stick/Right"] = "`Left X+`",
        ["Main Stick/Calibration"] = "100.00",
        ["C-Stick/Up"] = "`Right Y+`", ["C-Stick/Down"] = "`Right Y-`", ["C-Stick/Left"] = "`Right X-`", ["C-Stick/Right"] = "`Right X+`",
        ["C-Stick/Calibration"] = "100.00",
        ["Triggers/L"] = "`Trigger L`", ["Triggers/R"] = "`Trigger R`",
        ["Triggers/L-Analog"] = "`Trigger L`", ["Triggers/R-Analog"] = "`Trigger R`",
        ["D-Pad/Up"] = "`Pad N`", ["D-Pad/Down"] = "`Pad S`", ["D-Pad/Left"] = "`Pad W`", ["D-Pad/Right"] = "`Pad E`",
        ["Rumble/Motor"] = "`Motor L` | `Motor R`",
    };

    /// <summary>A stick's range as a square (the emulator writes eight reaches around the circle, in percent).</summary>
    private const string SquareRange = "100.00 141.42 100.00 141.42 100.00 141.42 100.00 141.42";

    /// <summary>The standard layout for a device: the keyboard's, a gamepad's (<paramref name="gamepad"/>: the
    /// runner's profile, or <see cref="Gamepad"/>), or none: a stick or a wheel is mapped by hand.</summary>
    public static IReadOnlyDictionary<string, string>? For(ControllerDevice device, IReadOnlyDictionary<string, string>? gamepad = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        return device.Keyboard ? Keyboard : device.Gamepad ? gamepad ?? Gamepad : null;
    }

    /// <summary>Whether a port's mapping is the device's standard layout, key for key (every key a layout owns:
    /// <see cref="PadControls.LayoutKeys"/>); false for a device that has none.</summary>
    public static bool IsStandard(PortSettings port, ControllerDevice device, IReadOnlyDictionary<string, string>? gamepad = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        return For(device, gamepad) is { } layout && PadControls.LayoutKeys.All(key =>
            (port.Map.TryGetValue(key, out var set) ? set.Trim() : "") == (layout.TryGetValue(key, out var standard) ? standard.Trim() : ""));
    }

    /// <summary>A port set to a device with its standard layout; a device that has none keeps the port's mapping.</summary>
    public static PortSettings Applied(PortSettings port, ControllerDevice device, IReadOnlyDictionary<string, string>? gamepad = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        var layout = For(device, gamepad);
        return port with
        {
            Kind = PortKind.Controller,
            Device = device.Id,
            Map = layout is null ? port.Map : new Dictionary<string, string>(layout, StringComparer.Ordinal),
        };
    }

    /// <summary>Port 1 of a game that has no controller settings at all: the first gamepad found with the standard
    /// layout (a GameCube controller on the adapter: the adapter itself, which needs none), else the keyboard with
    /// its own.</summary>
    /// <param name="devices">What is plugged in; none known: the keyboard.</param>
    public static PortSettings FirstStart(IReadOnlyList<ControllerDevice> devices, IReadOnlyDictionary<string, string>? gamepad = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var first = devices.FirstOrDefault(d => d.Gamepad && (d.Offered || d.OnAdapter));
        if (first is { OnAdapter: true })
        {
            return PortSettings.Off with { Kind = PortKind.Adapter };
        }
        var device = first
            ?? devices.FirstOrDefault(d => d.Keyboard)
            ?? new ControllerDevice(KeyboardDevice, "DInput", 0, "Keyboard Mouse", false, false);
        return Applied(PortSettings.Off, device, gamepad);
    }
}
