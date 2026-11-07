namespace Launcher.Core;

/// <summary>One file the runner lays over the disc at boot: its path under <c>files/</c> and the file it takes the
/// bytes from (a whole path: the runner is a Windows program). The launch file's <c>overlay</c>. RaceSame: a car data
/// or course file whose race fields the guard found equal to the disc's (only look fields differ): the runner takes
/// such a file in every start, online included, and no other car data or course file outside offline play.</summary>
public sealed record OverlayFile(string Disc, string From, bool RaceSame = false);

/// <summary>Which game start an overlay is for: offline play ("Start F-Zero GX"), where the whole mod is used, or any
/// other (a race, Practice, a replay, watching, the game's controller options), where only what passed the guard is.</summary>
public enum OverlayUse
{
    Offline,
    Guarded,
}

/// <summary>
/// The overlays of the mods that are on, in the player's order, the file of a mod higher on the Mods page over a lower
/// one's (the owner, 2026-11-05: mods on top take priority; and in a mod, its ticked parts' files:
/// <see cref="ModManifest.Files"/>): the
/// launch file's <c>overlay</c> (protocol/launch.toml) for each kind of start. With no mod on, none, and the launch
/// file is as it was. Code is never in either; the runner refuses it too.
/// </summary>
public sealed record ModOverlay(IReadOnlyList<OverlayFile> Offline, IReadOnlyList<OverlayFile> Guarded)
{
    public static ModOverlay None { get; } = new([], []);

    /// <summary>The overlay of a start.</summary>
    public IReadOnlyList<OverlayFile> For(OverlayUse use) => use == OverlayUse.Offline ? Offline : Guarded;

    /// <summary>Whether one file of a mod is used in a start that is not offline play.</summary>
    public static bool UsedOnline(ModFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        // a car data or course file that passed the guard goes too, marked raceSame (the owner, 2026-11-03: mods work
        // online; the runner takes such a file outside offline play only so marked)
        return file.Online && file.Class != ModFileClass.Code;
    }

    /// <summary>The overlays of the mods that are on.</summary>
    public static ModOverlay Of(IEnumerable<InstalledMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        var offline = new List<OverlayFile>();
        var guarded = new List<OverlayFile>();
        // the bottom mod first, so the top one's file is the last of its disc path, the one kept
        foreach (var mod in mods.Where(m => m.On).Reverse())
        {
            foreach (var file in mod.Manifest.Files.Where(f => f.Class != ModFileClass.Code))
            {
                var raceSame = file.Online && file.Class is ModFileClass.CarData or ModFileClass.Course;
                var entry = new OverlayFile(file.Disc, Path.GetFullPath(mod.PathOf(file)), raceSame);
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
