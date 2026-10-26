namespace Launcher.Core;

// One question to the controller helper, the launcher's sentences about the controllers, and what a game start needs
// of them (the helper itself: ControllerHelper.cs).

/// <summary>How the controllers stand at one moment, as the helper said it.</summary>
public sealed record ControllerSnapshot(IReadOnlyList<ControllerDevice> Devices, string DefaultDevice,
    IReadOnlyList<HelperPort> Ports, HelperAdapter? Adapter = null);

/// <summary>One question to a helper started for it: what is plugged in, and how the ports stand.</summary>
public static class ControllerProbe
{
    /// <summary>How long the helper has to say its first state.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(6);

    /// <summary>How long the adapter has to be found once asked for (the game's input layer looks twice a second).</summary>
    public static readonly TimeSpan AdapterLimit = TimeSpan.FromSeconds(2);

    /// <param name="start">Starts the helper (<see cref="ControllerHelper.Start"/>, or a test's double).</param>
    /// <param name="adapter">Also ask for the GameCube adapter when a port is set to it.</param>
    /// <param name="cancel">The asker is going: the helper is ended at once and the answer is null.</param>
    /// <returns>The state; null when there is no helper, or it said nothing in time.</returns>
    public static async Task<ControllerSnapshot?> AskAsync(Func<ControllerHelper?> start, bool adapter = false, TimeSpan? limit = null,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        using var helper = start();
        if (helper is null)
        {
            return null;
        }
        var gate = new Lock();
        HelperDevices? devices = null;
        HelperPorts? ports = null;
        HelperAdapter? adapterSaid = null;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var found = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        helper.Received += message =>
        {
            lock (gate)
            {
                switch (message)
                {
                    case HelperDevices d: devices = d; break;
                    case HelperPorts p: ports = p; break;
                    case HelperAdapter a:
                        adapterSaid = a;
                        if (a.State == AdapterState.Found)
                        {
                            found.TrySetResult();
                        }
                        break;
                    case HelperReady: ready.TrySetResult(); break;
                    default: break;
                }
            }
        };
        helper.Ended += () => { ready.TrySetResult(); found.TrySetResult(); };
        helper.Listen();
        await Task.WhenAny(ready.Task, Task.Delay(limit ?? Limit, cancel)).ConfigureAwait(false);
        if (cancel.IsCancellationRequested)
        {
            return null;
        }
        bool wanted;
        lock (gate)
        {
            wanted = adapter && ports is not null && ports.Ports.Any(p => p.Kind == PortSettings.AdapterNumber);
        }
        if (wanted)
        {
            helper.AskAdapter();
            await Task.WhenAny(found.Task, Task.Delay(AdapterLimit, cancel)).ConfigureAwait(false);
        }
        if (cancel.IsCancellationRequested)
        {
            return null;
        }
        lock (gate)
        {
            return devices is null || ports is null
                ? null
                : new ControllerSnapshot(devices.Devices, devices.Default, ports.Ports, wanted ? adapterSaid : null);
        }
    }
}

/// <summary>What the launcher says of the controllers, in a player's words.</summary>
public static class ControllerWords
{
    /// <summary>The GameCube adapter's state, as a label.</summary>
    public static string Adapter(HelperAdapter? adapter) => adapter?.State switch
    {
        null => "Looking for the adapter…",
        AdapterState.Found => "Adapter ready",
        AdapterState.None => "No GameCube adapter",
        AdapterState.NoDriver => "No driver: WinUSB",
        AdapterState.InUse => "In use by another program",
        AdapterState.Error => "Adapter not opened",
        AdapterState.Unused => "No port set to the adapter",
        _ => "Adapter left to the game",
    };

    /// <summary>The value beside the adapter's label: what a player needs to know of it, in a word or two (the
    /// adapter's switch; the driver's tool; the error's own words). A found adapter's value is its port's, which the
    /// page knows (<see cref="AdapterPort"/>).</summary>
    public static string AdapterValue(HelperAdapter? adapter) => adapter?.State switch
    {
        AdapterState.None => "Wii U switch",
        AdapterState.NoDriver => "Zadig",
        AdapterState.Error => adapter.Detail ?? "",
        _ => "",
    };

    /// <summary>A found adapter's port: whether a controller is in it.</summary>
    public static string AdapterPort(bool controllerIn, int port) => controllerIn
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Controller in port {port}")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"No controller in port {port}");

    /// <summary>A port whose mapping is its device's standard layout (the board's word for it).</summary>
    public const string Standard = "Standard";

    /// <summary>What the page says of a port's layout, under "Use the standard layout": the standard layout
    /// only while the port's mapping is that layout; a mapping that differs from it is the player's own, and a device
    /// with no standard layout is set by hand. The sentence describes what is set, never what could be.</summary>
    /// <param name="device">The port's device when it is plugged in; null: nothing is said.</param>
    /// <param name="gamepad">The runner's own gamepad profile (<see cref="ControllerFiles.GamepadProfile"/>).</param>
    public static string Layout(PortSettings port, ControllerDevice? device, IReadOnlyDictionary<string, string>? gamepad = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (device is null)
        {
            return "";
        }
        if (StandardLayouts.For(device, gamepad) is null)
        {
            return "No standard layout";
        }
        if (!StandardLayouts.IsStandard(port, device, gamepad))
        {
            return "Your own layout";
        }
        return Standard;
    }

    /// <summary>The keys that are never a choice in "press to set": Esc cancels; Alt and the Windows key are how a
    /// player leaves the launcher's window (Alt+Tab, the Start menu), and are heard before the window knows it is
    /// left. By the names the game's input layer gives them, on Windows and elsewhere.</summary>
    private static readonly HashSet<string> NoChoice = new(StringComparer.OrdinalIgnoreCase)
    {
        "ESCAPE", "Escape", "Alt", "LMENU", "RMENU", "Alt_L", "Alt_R", "LWIN", "RWIN", "Super_L", "Super_R",
    };

    /// <summary>Whether what the helper heard is the player's choice of a control: an input was heard, and it is
    /// none of the keys that end or leave a listening, nor the mouse (a click on the page is no choice).</summary>
    public static bool IsChoice(HelperCaptured captured)
    {
        ArgumentNullException.ThrowIfNull(captured);
        return captured is { Input: { Length: > 0 } input, Expression.Length: > 0 }
            && !NoChoice.Contains(input)
            && !input.StartsWith("Click ", StringComparison.Ordinal) && !input.StartsWith("Cursor ", StringComparison.Ordinal);
    }

    /// <summary>A device by the name a player knows: the last part of <c>BACKEND/INDEX/NAME</c>.</summary>
    public static string DeviceName(string device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var parts = device.Split('/', 3);
        var name = parts.Length == 3 ? parts[2] : device;
        return name.Equals("Keyboard Mouse", StringComparison.OrdinalIgnoreCase) ? "the keyboard" : name;
    }

    /// <summary>Why port 1 would give the game no controller, as a sentence for before the race; null: it is fine,
    /// or nothing is known.</summary>
    public static string? Port1Problem(ControllerSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Ports.Count == 0)
        {
            return null;
        }
        var port = snapshot.Ports[0];
        switch (PortSettings.KindOf(port.Kind))
        {
            case PortKind.Off:
                return "Port 1 off · set a controller in Settings, Controller";
            case PortKind.Controller when port.Device.Length == 0:
                return "Port 1 has no controller · Settings, Controller";
            case PortKind.Controller when !port.Connected:
                return $"Not plugged in · {DeviceName(port.Device)}";
            case PortKind.Adapter when snapshot.Adapter is { State: AdapterState.Found } adapter && !adapter.Ports[0]:
                return "Adapter port 1 empty";
            case PortKind.Adapter when snapshot.Adapter is { State: not (AdapterState.Found or AdapterState.NotAsked or AdapterState.Unused) } adapter:
                return AdapterValue(adapter) is { Length: > 0 } value ? $"{Adapter(adapter)} · {value}" : Adapter(adapter);
            default:
                return null;
        }
    }
}

/// <summary>What a game start needs of the controllers, done before it.</summary>
public static class ControllerStart
{
    /// <summary>A first start gets a working controller: when the game has no controller settings and the fork's
    /// folder gave none (<see cref="RecompLaunch.PrepareUserFolder"/>), port 1 is set to the first gamepad found
    /// with the standard layout, else to the keyboard; when the helper gave no answer nothing is written, and the
    /// next start chooses. A player who has settings keeps them untouched, and is told when port 1 would give the
    /// game no controller.</summary>
    /// <param name="controllerFrom">The fork's user folder, whose settings the game takes once.</param>
    /// <param name="adapter">Ask for the GameCube adapter when port 1 is set to it.</param>
    /// <param name="start">Starts the helper; null: the game's own (<see cref="ControllerHelper.Start"/>).</param>
    /// <param name="cancel">The launcher is closing: the helper is ended and nothing more is written (a check that
    /// outlived its launcher wrote the game's controller settings into a folder that was being taken away).</param>
    /// <returns>The sentence for the player; null: nothing to say.</returns>
    public static async Task<string?> PrepareAsync(RecompPaths paths, string? controllerFrom, bool adapter = true,
        Func<ControllerHelper?>? start = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            if (cancel.IsCancellationRequested)
            {
                return null;
            }
            RecompLaunch.PrepareUserFolder(paths.UserFolder, controllerFrom);
            var first = !ControllerFiles.HasSettings(paths.UserFolder);
            var snapshot = await ControllerProbe.AskAsync(start ?? (() => ControllerHelper.Start(paths)), adapter && !first, cancel: cancel)
                .ConfigureAwait(false);
            if (cancel.IsCancellationRequested)
            {
                return null;
            }
            if (!first)
            {
                return ControllerWords.Port1Problem(snapshot);
            }
            // With no answer (no helper, one that died, one that was late) nothing is written: a choice made without
            // knowing what is plugged in would be kept for good, and a player with a gamepad would stay on the
            // keyboard. The next start chooses again; meanwhile the emulator falls back to the keyboard by itself.
            if (snapshot is not null)
            {
                ControllerFiles.WriteFirstStart(paths.UserFolder,
                    StandardLayouts.FirstStart(snapshot.Devices, ControllerFiles.GamepadProfile(paths.Runner)));
            }
            return null;
        }
        catch (Exception e) when (ControllerFiles.Failed(e))
        {
            return $"Controller settings not written · {e.Message}";
        }
    }
}
