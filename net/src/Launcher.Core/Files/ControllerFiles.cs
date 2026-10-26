using System.Globalization;

namespace Launcher.Core;

/// <summary>The game's controller settings as read: its four ports, and why they could not be read, if so.</summary>
public sealed record ControllerFileSet(IReadOnlyList<PortSettings> Ports, string? Problem = null);

/// <summary>
/// The game's controller settings on disk, in its user folder: which kind of device each port has
/// (<c>Config\Dolphin.ini</c>, <c>[Core] SIDevice0</c> to <c>SIDevice3</c>) and each port's device and mapping
/// (<c>Config\GCPadNew.ini</c>, <c>[GCPad1]</c> to <c>[GCPad4]</c>). The game reads these two files at its start, and
/// the Controller page writes them here and nowhere else. A write changes the lines of one port that the page knows
/// (<see cref="PadControls.LayoutKeys"/>, the device, the dead zones) and keeps every other line as it was: a setting
/// made by hand, or by a newer game, is never lost.
/// </summary>
public static class ControllerFiles
{
    public const int Ports = 4;

    /// <summary>The game's settings files are a few KiB: one longer than this is not read, and not written over.</summary>
    public const int MaxBytes = 1 << 20;

    private const string StickDeadZone = PadControls.StickDeadZone, CStickDeadZone = PadControls.CStickDeadZone;

    public static string PadFile(string userFolder) => Path.Combine(userFolder, "Config", "GCPadNew.ini");

    public static string MainFile(string userFolder) => Path.Combine(userFolder, "Config", "Dolphin.ini");

    /// <summary>Whether the game has controller settings of its own; a first start has none.</summary>
    public static bool HasSettings(string userFolder) => File.Exists(PadFile(userFolder));

    /// <summary>The four ports as the files have them. Never raises: a file that cannot be read gives ports that are
    /// off and the sentence that says so.</summary>
    public static ControllerFileSet Read(string userFolder)
    {
        try
        {
            var main = Text(MainFile(userFolder)).Lines;
            var pad = Text(PadFile(userFolder)).Lines;
            return new ControllerFileSet([.. Enumerable.Range(0, Ports).Select(port => PortOf(main, pad, port))]);
        }
        catch (Exception e) when (Failed(e))
        {
            return new ControllerFileSet([.. Enumerable.Repeat(PortSettings.Off, Ports)],
                $"The game's controller settings could not be read: {e.Message}");
        }
    }

    /// <summary>Writes one port. Raises what a write can (<see cref="Failed"/>): the caller says it.</summary>
    public static void Write(string userFolder, int port, PortSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(port, Ports);
        Directory.CreateDirectory(Path.Combine(userFolder, "Config"));
        Change(MainFile(userFolder), main => WithKind(main, port, settings));
        if (settings.Kind == PortKind.Controller)
        {
            // a port that is off or the adapter's keeps its mapping for the day it is a controller again
            Change(PadFile(userFolder), pad => WithPort(pad, port, settings));
        }
    }

    /// <summary>A first start's controller: port 1 written when the game has no controller settings at all, and its
    /// kind only where the game's settings name none. True when it was written; with settings there, nothing is
    /// touched.</summary>
    public static bool WriteFirstStart(string userFolder, PortSettings first)
    {
        ArgumentNullException.ThrowIfNull(first);
        if (HasSettings(userFolder))
        {
            return false;
        }
        Directory.CreateDirectory(Path.Combine(userFolder, "Config"));
        if (IniLines.Get(Text(MainFile(userFolder)).Lines, "Core", KindKey(0)) is null)
        {
            Change(MainFile(userFolder), main => WithKind(main, 0, first));
        }
        Change(PadFile(userFolder), pad => WithPort(pad, 0, first));
        return true;
    }

    /// <summary>The runner's own layout for a gamepad SDL knows (<c>Sys\Profiles\GCPad\SDL Gamepad.ini</c> beside the
    /// runner); null when it is not there or says nothing the page knows.</summary>
    public static IReadOnlyDictionary<string, string>? GamepadProfile(string runner)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(runner));
            var file = Path.Combine(folder ?? ".", "Sys", "Profiles", "GCPad", "SDL Gamepad.ini");
            var map = MapOf(Text(file).Lines, "Profile");
            return map.Count > 0 ? map : null;
        }
        catch (Exception e) when (Failed(e))
        {
            return null;
        }
    }

    /// <summary>One port of the two files' lines. A port the main settings do not name has the emulator's own kind:
    /// a controller in port 1, nothing in the others.</summary>
    public static PortSettings PortOf(IReadOnlyList<string> main, IReadOnlyList<string> pad, int port)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(pad);
        var number = IniLines.Get(main, "Core", KindKey(port)) is { } text
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 0 and < 64
            ? n
            : port == 0 ? PortSettings.ControllerNumber : PortSettings.NothingNumber;
        var section = Section(port);
        return new PortSettings(PortSettings.KindOf(number), IniFiles.FromFile(IniLines.Get(pad, section, "Device") ?? ""), MapOf(pad, section),
            Percent(IniLines.Get(pad, section, StickDeadZone)), Percent(IniLines.Get(pad, section, CStickDeadZone)), number);
    }

    /// <summary>The main settings with one port's kind set, every other line kept.</summary>
    public static IReadOnlyList<string> WithKind(IReadOnlyList<string> main, int port, PortSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return IniLines.Set(main, "Core", KindKey(port), settings.Number.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The controller settings with one port's device, mapping and dead zones set: a layout key the port
    /// does not map is taken out, and every line the page does not know is kept.</summary>
    public static IReadOnlyList<string> WithPort(IReadOnlyList<string> pad, int port, PortSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var section = Section(port);
        var lines = IniLines.Set(pad, section, "Device", settings.Device.Length > 0 ? OneLine(settings.Device) : null);
        foreach (var key in PadControls.LayoutKeys)
        {
            lines = IniLines.Set(lines, section, key, settings.Map.TryGetValue(key, out var value) && value.Trim().Length > 0 ? OneLine(value) : null);
        }
        lines = IniLines.Set(lines, section, StickDeadZone, PercentText(settings.StickDeadZone));
        return IniLines.Set(lines, section, CStickDeadZone, PercentText(settings.CStickDeadZone));
    }

    public static string Section(int port) => string.Create(CultureInfo.InvariantCulture, $"GCPad{port + 1}");

    private static string KindKey(int port) => string.Create(CultureInfo.InvariantCulture, $"SIDevice{port}");

    /// <summary>What a write or a read of these files can raise.</summary>
    public static bool Failed(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;

    private static Dictionary<string, string> MapOf(IReadOnlyList<string> pad, string section)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in PadControls.LayoutKeys)
        {
            if (IniLines.Get(pad, section, key) is { Length: > 0 } value)
            {
                map[key] = IniFiles.FromFile(value);
            }
        }
        return map;
    }

    /// <summary>A dead zone, in percent: 0 to 50, as the emulator takes it; anything else is none.</summary>
    private static double Percent(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 50 ? value : 0;

    private static string? PercentText(double value) =>
        value is > 0 and <= 50 ? value.ToString("0.0#", CultureInfo.InvariantCulture) : null;

    /// <summary>A value is one line of the file, whatever it holds, in the file's letters (UTF-8).</summary>
    private static string OneLine(string value) => IniFiles.ToFile(value.ReplaceLineEndings(" ").Trim());

    private static IniText Text(string file) => IniFiles.Read(file, MaxBytes);

    /// <summary>One file read, changed and written: only when a line differs from what it holds.</summary>
    private static void Change(string file, Func<IReadOnlyList<string>, IReadOnlyList<string>> change)
    {
        var was = Text(file);
        var lines = change(was.Lines);
        if (!File.Exists(file) || !lines.SequenceEqual(was.Lines))
        {
            IniFiles.Write(file, was, lines);
        }
    }
}
