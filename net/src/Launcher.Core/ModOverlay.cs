namespace Launcher.Core;

/// <summary>One file the runner lays over the disc at boot: its path under <c>files/</c> and the file it takes the
/// bytes from (a whole path: the runner is a Windows program). The launch file's <c>overlay</c>.</summary>
public sealed record OverlayFile(string Disc, string From);

/// <summary>Which game start an overlay is for: offline play ("Play F-Zero GX"), where the whole mod is used, or any
/// other (a race, Practice, a replay, watching, the game's controller options), where only what passed the guard is.</summary>
public enum OverlayUse
{
    Offline,
    Guarded,
}

/// <summary>
/// The overlays of the mods that are on, in the player's order, a later mod's file over an earlier one's: the
/// launch file's <c>overlay</c> (protocol/launch.toml) for each kind of start. With no mod on, none, and the launch
/// file is as it was. Code is never in either; the runner refuses it too.
/// </summary>
public sealed record ModOverlay(IReadOnlyList<OverlayFile> Offline, IReadOnlyList<OverlayFile> Guarded)
{
    public static ModOverlay None { get; } = new([], []);

    /// <summary>Whether a start that is not offline play takes a car data or course file that passed the guard. The
    /// runner's contract (2026-11-03) refuses <c>game/cardata.lz</c> and <c>stage/COLI_COURSE*.lz</c> in every start
    /// but offline play, whatever the guard said, so they are not sent there until the runner takes them.</summary>
    public const bool CheckedRaceFilesOnline = false;

    /// <summary>The overlay of a start.</summary>
    public IReadOnlyList<OverlayFile> For(OverlayUse use) => use == OverlayUse.Offline ? Offline : Guarded;

    /// <summary>Whether one file of a mod is used in a start that is not offline play.</summary>
    public static bool UsedOnline(ModFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.Online && file.Class != ModFileClass.Code &&
               (CheckedRaceFilesOnline || file.Class is not (ModFileClass.CarData or ModFileClass.Course));
    }

    /// <summary>The overlays of the mods that are on.</summary>
    public static ModOverlay Of(IEnumerable<InstalledMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        var offline = new List<OverlayFile>();
        var guarded = new List<OverlayFile>();
        foreach (var mod in mods.Where(m => m.On))
        {
            foreach (var file in mod.Manifest.Files.Where(f => f.Class != ModFileClass.Code))
            {
                var entry = new OverlayFile(file.Disc, Path.GetFullPath(Path.Combine(mod.Files, file.Disc)));
                offline.Add(entry);
                if (UsedOnline(file))
                {
                    guarded.Add(entry);
                }
            }
        }
        return new ModOverlay(LastWins(offline), LastWins(guarded));
    }

    /// <summary>The list with each disc path once, where its last entry stood: the runner would lay the later one over
    /// the earlier anyway, so the earlier is not sent.</summary>
    private static List<OverlayFile> LastWins(List<OverlayFile> files)
    {
        var last = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Count; i++)
        {
            last[files[i].Disc] = i;
        }
        return [.. files.Where((f, i) => last[f.Disc] == i)];
    }
}
