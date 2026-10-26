using System.Globalization;

namespace Launcher.Core;

// The game's user folder made ready: its emulator settings read, completed and written, the controller copied.
public static partial class RecompLaunch
{
    /// <summary>The emulator settings the game needs, written where the user folder has none yet; a setting already
    /// there (the player's volume, say) is left alone. Fixed by the module: the emulated CPU's clock (every check of
    /// the native code ran at 6x) and the operating system's idle loop.</summary>
    private static readonly (string Section, string Key, string Value)[] Defaults =
    [
        ("Core", "OverclockEnable", "True"),
        ("Core", "Overclock", "6.0"),
        ("Core", "StaticRecompIdlePC", "0x8001071C"),
        ("Core", "EmulationSpeed", "1.0"),
        ("Core", "EnableCheats", "False"),
        ("Core", "GFXBackend", "Vulkan"),
        ("Interface", "ConfirmStop", "False"),
        ("Interface", "UsePanicHandlers", "False"),
        ("Interface", "OnScreenDisplayMessages", "False"),
        ("DSP", "Backend", "Cubeb"),
        ("Analytics", "Enabled", "False"),
        ("Analytics", "PermissionAsked", "True"),
    ];

    /// <summary>The settings whose value the module fixes: written whatever the file says.</summary>
    private static readonly HashSet<string> Fixed = ["OverclockEnable", "Overclock", "StaticRecompIdlePC", "EnableCheats"];

    /// <summary>Makes the game's user folder ready: its emulator settings (see <see cref="Defaults"/>), the folder
    /// the emulator keeps its shader cache in, and the player's controller profile copied from
    /// <paramref name="controllerFrom"/> (the fork's user folder, where the player set their controller up) when the
    /// game has none of its own. Nothing is ever written outside <paramref name="userFolder"/>.</summary>
    /// <param name="volume">How loud the game is, 0 to 100 (the player's setting in the launcher): written as the
    /// emulator's own volume (<c>[DSP] Volume</c>, which the runner leaves to the user folder); null: the file's
    /// own is left. A start with the runner's <c>--mute</c> (the tests') is silent whatever is written here: the
    /// runner sets the run's volume to nothing after it has read the file.</param>
    public static void PrepareUserFolder(string userFolder, string? controllerFrom = null, int? volume = null)
    {
        var config = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(config);
        // The runner never makes this folder, and the emulator's shader cache makes only the one inside it: without
        // it nothing was kept, and every start built its 570 shaders and pipelines anew, 180 of them during the
        // first race (5 with the cache from the start before).
        Directory.CreateDirectory(Path.Combine(userFolder, "Cache"));
        var ini = Path.Combine(config, "Dolphin.ini");
        var theirSettings = controllerFrom is null ? null : Path.Combine(controllerFrom, "Config", "Dolphin.ini");
        // (read and written without loss: a line in letters that are no UTF-8 comes through as it was)
        var was = IniFiles.Read(ini, ControllerFiles.MaxBytes);
        var has = File.Exists(ini) ? was.Lines : null;
        var wanted = WithPorts(WithDefaults(has ?? []),
            theirSettings is not null ? IniFiles.Read(theirSettings, ControllerFiles.MaxBytes).Lines : []);
        // Written only when a line differs, and then beside the file and moved over it. The Controller page makes the
        // folder ready each time it shows, also while a game runs or starts: a game must never read a file cut short,
        // without the module's fixed settings (the emulated CPU's clock).
        if (has is null || !has.SequenceEqual(wanted))
        {
            IniFiles.Write(ini, was, wanted);
        }
        // The volume is the player's wish, not what the game needs: a file that cannot be written (read-only, a
        // folder that takes no new file) keeps the volume it had and the game starts all the same (HasVolume says).
        if (volume is { } loud && WithVolume(wanted, loud) is var louder && !louder.SequenceEqual(wanted))
        {
            try
            {
                IniFiles.Write(ini, IniFiles.Read(ini, ControllerFiles.MaxBytes), louder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // (the file is as it was: IniFiles.Write leaves nothing beside it)
            }
        }
        var pad = Path.Combine(config, "GCPadNew.ini");
        if (!File.Exists(pad) && controllerFrom is not null)
        {
            var theirs = Path.Combine(controllerFrom, "Config", "GCPadNew.ini");
            if (File.Exists(theirs))
            {
                File.Copy(theirs, pad);
            }
        }
    }

    /// <summary>An emulator settings file with the game's settings in: each of <see cref="Defaults"/> added where its
    /// key is missing, the module's fixed ones set, every other line kept as it was.</summary>
    public static IReadOnlyList<string> WithDefaults(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new List<string>(lines);
        foreach (var (section, key, value) in Defaults)
        {
            var header = result.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
            if (header < 0)
            {
                result.Add($"[{section}]");
                result.Add($"{key} = {value}");
                continue;
            }
            var end = result.FindIndex(header + 1, l => l.TrimStart().StartsWith('['));
            end = end < 0 ? result.Count : end;
            var at = result.FindIndex(header + 1, end - header - 1,
                l => l.Split('=', 2) is { Length: 2 } parts && parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase));
            if (at < 0)
            {
                result.Insert(header + 1, $"{key} = {value}");
            }
            else if (Fixed.Contains(key))
            {
                result[at] = $"{key} = {value}";
            }
        }
        return result;
    }

    /// <summary>The settings with the game's volume set (<c>[DSP] Volume</c>, 0 to 100), every other line kept. A
    /// file can hold the section or the key more than once (the emulator reads the last): every one is set.</summary>
    public static IReadOnlyList<string> WithVolume(IReadOnlyList<string> lines, int volume)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var line = string.Create(CultureInfo.InvariantCulture, $"Volume = {Math.Clamp(volume, 0, 100)}");
        var result = new List<string>(lines);
        var sections = 0;
        for (var header = 0; header < result.Count; header++)
        {
            if (!result[header].Trim().Equals("[DSP]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            sections++;
            var end = result.FindIndex(header + 1, l => l.TrimStart().StartsWith('['));
            end = end < 0 ? result.Count : end;
            var found = false;
            for (var i = header + 1; i < end; i++)
            {
                if (result[i].Split('=', 2) is { Length: 2 } parts && parts[0].Trim().Equals("Volume", StringComparison.OrdinalIgnoreCase))
                {
                    result[i] = line;
                    found = true;
                }
            }
            if (!found)
            {
                result.Insert(header + 1, line);
            }
        }
        if (sections == 0)
        {
            result.Add("[DSP]");
            result.Add(line);
        }
        return result;
    }

    /// <summary>Whether the game's settings hold this volume (after <see cref="PrepareUserFolder"/>: a settings file
    /// that could not be written keeps the volume it had, and the launcher says so).</summary>
    public static bool HasVolume(string userFolder, int volume)
    {
        var lines = IniFiles.Read(Path.Combine(userFolder, "Config", "Dolphin.ini"), ControllerFiles.MaxBytes).Lines;
        return lines.SequenceEqual(WithVolume(lines, volume));
    }

    /// <summary>The settings with the device of each controller port (<c>[Core] SIDevice0</c> to <c>SIDevice3</c>:
    /// none, a standard controller, the GameCube adapter, ...) taken from <paramref name="theirs"/> wherever the game's
    /// settings name none. A button profile alone does not say which ports are in use or that a port is the adapter's:
    /// without this a player's adapter, or their second controller, was lost. A port the game's settings already name
    /// is the player's to change and is left.</summary>
    public static IReadOnlyList<string> WithPorts(IReadOnlyList<string> lines, IReadOnlyList<string> theirs)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(theirs);
        var result = new List<string>(lines);
        for (var port = 0; port < 4; port++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"SIDevice{port}");
            if (Setting(result, "Core", key) is not null || Setting(theirs, "Core", key) is not { } device
                || !int.TryParse(device, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }
            var header = result.FindIndex(l => l.Trim().Equals("[Core]", StringComparison.OrdinalIgnoreCase));
            if (header < 0)
            {
                result.Add("[Core]");
                header = result.Count - 1;
            }
            result.Insert(header + 1, $"{key} = {device}");
        }
        return result;
    }

    /// <summary>The settings file's value of a key (tests).</summary>
    public static string? Setting(IReadOnlyList<string> lines, string section, string key)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var inside = false;
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith('['))
            {
                inside = t.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
            }
            else if (inside && t.Split('=', 2) is { Length: 2 } parts && parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1].Trim().ToString(CultureInfo.InvariantCulture);
            }
        }
        return null;
    }
}
