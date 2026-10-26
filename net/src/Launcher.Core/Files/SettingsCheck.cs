using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The settings file is a file on the player's disk: a hand's edit, a half-written disk or somebody else's file can
/// put anything in it, and every page reads it when the launcher opens. <see cref="Sound"/> gives settings every
/// page can read without a check of its own: nothing null that is "never null", every number in its range, every
/// choice one of the choices, every text of a length a text box holds. What was not sound goes back to its default
/// (the rest of the file stays), and the caller is told that something did.
/// </summary>
public static class SettingsCheck
{
    /// <summary>The longest path kept (Windows' long paths end far under it).</summary>
    public const int MostPath = 1024;

    /// <summary>The longest server address kept.</summary>
    public const int MostServer = 512;

    /// <summary>The longest name kept: the name box's length (the game shows 16 characters of it).</summary>
    public const int MostName = 40;

    /// <summary>The settings with everything unsound set back to its default, and whether anything was.</summary>
    public static (LauncherSettings Settings, bool Repaired) Sound(LauncherSettings? read)
    {
        if (read is null)
        {
            return (new LauncherSettings { NoServer = true }, true);
        }
        var defaults = new LauncherSettings();
        // an address that is none is not the official server's: there is then no server (LauncherSettings.NoServer)
        var noServer = read.NoServer || (!string.IsNullOrWhiteSpace(read.Server) && !IsServer(read.Server));
        var sound = read with
        {
            Racer = Racer(read.Racer),
            DiscPath = Text(read.DiscPath, MostPath),
            UserFolder = Text(read.UserFolder, MostPath),
            ReplayFolder = Text(read.ReplayFolder, MostPath),
            Recomp = read.Recomp is { } recomp
                ? recomp with
                {
                    Runner = Text(recomp.Runner, MostPath),
                    Module = Text(recomp.Module, MostPath),
                    GameFolder = Text(recomp.GameFolder, MostPath),
                    UserFolder = Text(recomp.UserFolder, MostPath),
                    Bundle = Text(recomp.Bundle, MostPath),
                }
                : null,
            Server = !noServer && IsServer(read.Server) ? read.Server : null,
            NoServer = noServer,
            OtherSounds = Enum.IsDefined(read.OtherSounds) ? read.OtherSounds : defaults.OtherSounds,
            SettingsTab = Enum.IsDefined(read.SettingsTab) ? read.SettingsTab : defaults.SettingsTab,
            InterfaceScale = read.ShownInterfaceScale,
            // a stat this launcher does not know (a newer one's, a slip of the hand): by number
            GarageSort = read.GarageSort is { } sort && MachineStats.Has(sort.By) ? sort : MachineSort.ByNumber,
            FavouriteMachines = Favourites(read.FavouriteMachines),
            Practice = read.Practice?.Kept(),
            GarageStats = Online.Protocol.StatPack.IsName(read.GarageStats) ? read.GarageStats : null,
            SoundVolume = read.SoundVolume is >= 0 and <= 100 ? read.SoundVolume : defaults.SoundVolume,
            GameVolume = read.GameVolume is >= 0 and <= 100 ? read.GameVolume : defaults.GameVolume,
            // the input delay is the player's 0, 1 or 2 and never more; anything else is 2
            InputDelay = read.RaceInputDelay,
            Present = read.GamePresent ?? defaults.Present,   // (one of the two other ways, or a console's)
            RefreshRate = read.GameRefreshRate ?? defaults.RefreshRate,   // (120, 240, the display's, or the game's)
            FullScreen = read.GameFullScreen,
        };
        return (sound, !sound.Equals(read));
    }

    /// <summary>Whether a text is an address Quick Play can connect to: a WebSocket address with a host
    /// ("ws://192.168.1.20:47810/ws"). The Settings page keeps only such an address (or none: the official server).</summary>
    public static bool IsServer(string? text) =>
        text is { Length: > 0 and <= MostServer } && !text.Any(char.IsControl)
        && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "ws" or "wss" && uri.Host.Length > 0;

    /// <summary>A text as kept, or null when it is none a text box could have written (too long, or with control
    /// characters in it).</summary>
    private static string? Text(string? text, int most) =>
        text is null || (text.Length <= most && !text.Any(char.IsControl)) ? text : null;

    private static RacerProfile Racer(RacerProfile? read)
    {
        var standard = RacerProfile.Default;
        if (read is null)
        {
            return standard;
        }
        return read with
        {
            Name = Text(read.Name, MostName) is { } name && PlayerNames.Shown(name).Length > 0 ? name : standard.Name,
            Machine = IsMachine(read.Machine) ? read.Machine : standard.Machine,
            Color = read.Color ?? standard.Color,
            Setting = read.Setting is >= 0 and <= 100 ? read.Setting : standard.Setting,
            StockColor = read.StockColor is null or (>= 0 and <= 3) ? read.StockColor : standard.StockColor,
            Looks = Looks(read.Looks),
            Custom = CustomMachine.Checked(read.Custom),
            Customs = Customs(read.Customs),
        };
    }

    /// <summary>The builder's saved machines: each one the game can build, at most
    /// <see cref="RacerProfile.MostCustoms"/>. The same list when nothing in it was wrong.</summary>
    private static IReadOnlyList<CustomBuild>? Customs(IReadOnlyList<CustomBuild>? read)
    {
        if (read is null)
        {
            return null;
        }
        var kept = read.Where(b => b is { Sound: true }).Take(RacerProfile.MostCustoms).ToList();
        return kept.SequenceEqual(read) ? read : kept;
    }

    /// <summary>The garage's looks: one for a machine (the last, as <see cref="RacerProfile.LookOf"/> reads them),
    /// each of a machine there is, in a colour there is. The same list when nothing in it was wrong.</summary>
    private static IReadOnlyList<MachineLook>? Looks(IReadOnlyList<MachineLook>? read)
    {
        if (read is null)
        {
            return null;
        }
        var kept = new List<MachineLook>();
        for (var i = read.Count - 1; i >= 0; i--)
        {
            if (read[i] is { } look && IsMachine(look.Machine) && kept.TrueForAll(k => k.Machine != look.Machine))
            {
                var stock = look.StockColor is >= 0 and <= 3 ? look.StockColor : null;
                kept.Add(look with
                {
                    // neither a stock colour nor a paint: its own colour
                    StockColor = stock ?? (look.Color is null ? 0 : (int?)null),
                    Setting = look.Setting is >= 0 and <= 100 ? look.Setting : 100,
                });
            }
        }
        kept.Reverse();
        return kept.SequenceEqual(read) ? read : kept;
    }

    /// <summary>The marked machines: each a machine there is, once. The same list when nothing in it was wrong.</summary>
    private static IReadOnlyList<int>? Favourites(IReadOnlyList<int>? read)
    {
        if (read is null)
        {
            return null;
        }
        var kept = read.Where(IsMachine).Distinct().ToList();
        return kept.SequenceEqual(read) ? read : kept;
    }

    private static bool IsMachine(int machine) => Machines.Has(machine);
}
