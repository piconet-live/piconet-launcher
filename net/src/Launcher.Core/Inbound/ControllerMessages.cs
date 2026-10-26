using System.Text.Json;

namespace Launcher.Core;

/// <summary>Something the controller helper said (<see cref="ControllerHelper"/>).</summary>
public abstract record ControllerMessage;

/// <summary>Its first state has been said: what came before is how things stand.</summary>
public sealed record HelperReady(int Version) : ControllerMessage;

/// <summary>The devices plugged in now, and the one the game takes when a port names none.</summary>
public sealed record HelperDevices(IReadOnlyList<ControllerDevice> Devices, string Default) : ControllerMessage;

/// <summary>One port as the game reads its settings: its kind's number, its device, and whether that device is there.</summary>
public sealed record HelperPort(int Kind, string Device, bool Connected);

public sealed record HelperPorts(IReadOnlyList<HelperPort> Ports) : ControllerMessage;

/// <summary>What the game's input layer says of the GameCube adapter.</summary>
public enum AdapterState
{
    /// <summary>The launcher did not ask for it: it was not opened.</summary>
    NotAsked,

    /// <summary>No port is set to it, so the game does not look for it.</summary>
    Unused,
    Found,

    /// <summary>None is plugged in.</summary>
    None,

    /// <summary>One is plugged in, and Windows has no driver the game can use it with.</summary>
    NoDriver,

    /// <summary>One is plugged in, and another program holds it.</summary>
    InUse,

    /// <summary>It could not be opened, for the reason in the message's detail.</summary>
    Error,
}

/// <param name="Detail">The input layer's own words when it could not be opened.</param>
/// <param name="Ports">Which of the adapter's four ports have a controller.</param>
public sealed record HelperAdapter(AdapterState State, string Detail, IReadOnlyList<bool> Ports) : ControllerMessage;

/// <summary>The GameCube controller of one port as the game would read it now: the buttons held (A, B, X, Y, Z,
/// Start, L, R, Up, Down, Left, Right), both sticks (0 to 255, 128 at rest) and both triggers (0 to 255).</summary>
public sealed record HelperPad(int Port, int Kind, bool Connected, IReadOnlySet<string> Buttons, int StickX, int StickY,
    int CStickX, int CStickY, int L, int R) : ControllerMessage;

/// <summary>The input the player moved while the helper listened, with the text the game's settings name it by;
/// both null: none in time.</summary>
/// <param name="Id">The number the launcher gave that listening (<see cref="ControllerHelper.Capture"/>): an answer
/// to one that was ended meanwhile is told from the answer to the one that waits.</param>
public sealed record HelperCaptured(int Id, string Device, string? Input, string? Expression) : ControllerMessage;

/// <summary>The helper could not do what it was asked.</summary>
public sealed record HelperProblem(string Text) : ControllerMessage;

/// <summary>
/// The controller helper's lines, checked before the launcher keeps or shows any of them. The helper is the game's
/// own program, but what it says holds names that come from whatever is plugged in (a device names itself), so a
/// line is taken in within a length, its lists within a count, its numbers within their range, and a line that is
/// no message is nothing.
/// </summary>
public static class ControllerMessages
{
    public const int MaxLineLength = 64 * 1024, MaxDevices = 64, MaxNameLength = 200;

    private static readonly string[] ButtonNames = ["A", "B", "X", "Y", "Z", "Start", "L", "R", "Up", "Down", "Left", "Right"];

    /// <summary>The message of one line; null when it is none this launcher knows.</summary>
    public static ControllerMessage? Checked(string? line)
    {
        if (line is null || line.Length > MaxLineLength || !line.AsSpan().TrimStart().StartsWith("{"))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 6 });
            var root = document.RootElement;
            return Text(root, "is") switch
            {
                "ready" => new HelperReady(Whole(root, "version", 0, 1000)),
                "devices" => Devices(root),
                "ports" => Ports(root),
                "adapter" => Adapter(root),
                "pad" => Pad(root),
                "captured" => new HelperCaptured(Whole(root, "id", 0, int.MaxValue), Text(root, "device"), TextOrNull(root, "input"), TextOrNull(root, "expression")),
                "problem" => new HelperProblem(Text(root, "text")),
                _ => null,
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static HelperDevices Devices(JsonElement root)
    {
        var devices = new List<ControllerDevice>();
        foreach (var d in List(root, "devices", MaxDevices))
        {
            var id = Text(d, "id");
            if (id.Length > 0)
            {
                devices.Add(new ControllerDevice(id, Text(d, "backend"), Whole(d, "index", 0, 1000), Text(d, "name"), Flag(d, "gamepad"), Flag(d, "rumble")));
            }
        }
        return new HelperDevices(devices, Text(root, "default"));
    }

    private static HelperPorts Ports(JsonElement root)
    {
        var ports = List(root, "ports", ControllerFiles.Ports)
            .Select(p => new HelperPort(Whole(p, "kind", 0, 63), Text(p, "device"), Flag(p, "connected"))).ToList();
        while (ports.Count < ControllerFiles.Ports)
        {
            ports.Add(new HelperPort(0, "", false));
        }
        return new HelperPorts(ports);
    }

    private static HelperAdapter Adapter(JsonElement root)
    {
        var state = Text(root, "state") switch
        {
            "unused" => AdapterState.Unused,
            "found" => AdapterState.Found,
            "none" => AdapterState.None,
            "noDriver" => AdapterState.NoDriver,
            "inUse" => AdapterState.InUse,
            "error" => AdapterState.Error,
            _ => AdapterState.NotAsked,
        };
        var ports = List(root, "ports", ControllerFiles.Ports).Select(p => p.ValueKind == JsonValueKind.True).ToList();
        while (ports.Count < ControllerFiles.Ports)
        {
            ports.Add(false);
        }
        return new HelperAdapter(state, Text(root, "detail"), ports);
    }

    private static HelperPad Pad(JsonElement root)
    {
        var buttons = List(root, "buttons", 32).Where(b => b.ValueKind == JsonValueKind.String)
            .Select(b => b.GetString()!).Where(ButtonNames.Contains).ToHashSet(StringComparer.Ordinal);
        var (x, y) = Pair(root, "stick");
        var (cx, cy) = Pair(root, "cStick");
        return new HelperPad(Whole(root, "port", 0, ControllerFiles.Ports - 1), Whole(root, "kind", 0, 63), Flag(root, "connected"),
            buttons, x, y, cx, cy, Whole(root, "l", 0, 255), Whole(root, "r", 0, 255));
    }

    private static (int X, int Y) Pair(JsonElement root, string name)
    {
        var both = List(root, name, 2);
        return both.Count == 2 ? (Byte(both[0]), Byte(both[1])) : (128, 128);
    }

    private static int Byte(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? Math.Clamp(n, 0, 255) : 128;

    /// <summary>A list's first members, at most so many.</summary>
    private static List<JsonElement> List(JsonElement root, string name, int most) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Take(most)]
            : [];

    /// <summary>A text within the longest name, as plain text: no control character reaches a page.</summary>
    private static string Text(JsonElement root, string name) => TextOrNull(root, name) ?? "";

    private static string? TextOrNull(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var text = value.GetString() ?? "";
        text = text.Length > MaxNameLength ? text[..MaxNameLength] : text;
        return string.Concat(text.Where(c => !char.IsControl(c)));
    }

    private static bool Flag(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int Whole(JsonElement root, string name, int least, int most) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var n)
            ? Math.Clamp(n, least, most)
            : least;
}
