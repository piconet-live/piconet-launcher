using Launcher.Core;

namespace Launcher.App;

// The player's mods (Launcher.Core ModLibrary): the list, read once off the window's thread and kept; its changes
// saved at once; and the overlay every game start takes from it (PathsOf).
public sealed partial class AppState
{
    private readonly Lock _modsGate = new();
    private IReadOnlyList<InstalledMod>? _mods;
    private ModOverlay _overlay = ModOverlay.None;

    /// <summary>The data folder's mods.</summary>
    public ModLibrary ModLibrary => _library ??= new ModLibrary(dataFolder);

    private ModLibrary? _library;

    /// <summary>Said when the mods' list changed (an import, a switch, the order, a removal).</summary>
    public event Action? ModsChanged;

    /// <summary>The mods in the player's order; null until the first read has finished (<see cref="LoadModsAsync"/>).</summary>
    public IReadOnlyList<InstalledMod>? Mods
    {
        get
        {
            lock (_modsGate)
            {
                return _mods;
            }
        }
    }

    /// <summary>The overlay of the mods that are on: what <see cref="PathsOf"/> hands every start.</summary>
    private ModOverlay Overlay
    {
        get
        {
            lock (_modsGate)
            {
                return _overlay;
            }
        }
    }

    /// <summary>The first read of the mods' list, started with the launcher (<see cref="LoadModsAsync"/>): a start from
    /// the command line waits for it, so its game gets the overlay of the mods that are on.</summary>
    public Task ModsRead { get; private set; } = Task.CompletedTask;

    /// <summary>Reads the mods' list from the disk, on another thread.</summary>
    public Task LoadModsAsync() => Task.Run(() => SetMods(ReadModsQuietly(), save: false));

    private IReadOnlyList<InstalledMod> ReadModsQuietly()
    {
        try
        {
            return ModLibrary.Read();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void SetMods(IReadOnlyList<InstalledMod> mods, bool save)
    {
        lock (_modsGate)
        {
            _mods = mods;
            _overlay = ModOverlay.Of(mods);
        }
        if (save)
        {
            try
            {
                ModLibrary.Save(mods);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Report("Mods not saved");
            }
        }
        ModsChanged?.Invoke();
    }

    /// <summary>A mod switched on or off.</summary>
    public void SetModOn(string id, bool on) => ChangeMods(list => [.. list.Select(m => m.Manifest.Id == id ? m with { On = on } : m)]);

    /// <summary>A mod's parts ticked (the rest not): saved at once, used from the next game start.</summary>
    public void SetModParts(string id, IReadOnlySet<string> on) =>
        ChangeMods(list => [.. list.Select(m => m.Manifest.Id == id ? m with { Manifest = m.Manifest.WithOn(on) } : m)]);

    /// <summary>A mod moved one place up (-1) or down (1): a later mod's files win.</summary>
    public void MoveMod(string id, int by) => ChangeMods(list =>
    {
        var mods = list.ToList();
        var at = mods.FindIndex(m => m.Manifest.Id == id);
        var to = at + by;
        if (at < 0 || to < 0 || to >= mods.Count)
        {
            return list;
        }
        (mods[at], mods[to]) = (mods[to], mods[at]);
        return mods;
    });

    private void ChangeMods(Func<IReadOnlyList<InstalledMod>, IReadOnlyList<InstalledMod>> change)
    {
        var now = Mods ?? [];
        SetMods(change(now), save: true);
    }

    /// <summary>A mod removed, its folder deleted (off the window's thread).</summary>
    public Task RemoveModAsync(string id) => Task.Run(() =>
    {
        if ((Mods ?? []).FirstOrDefault(m => m.Manifest.Id == id) is not { } mod)
        {
            return;
        }
        try
        {
            ModLibrary.Remove(mod);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Report("Mod not removed");
            return;
        }
        ChangeMods(list => [.. list.Where(m => m.Manifest.Id != id)]);
    });

    /// <summary>The disc's files the import compares a mod with: the prepared game's <c>files</c> folder.</summary>
    public string GameFilesFolder => Path.Combine(GamePaths.GameFolder, "files");

    /// <summary>The first look at a mod, off the window's thread.</summary>
    public Task<(ModSurvey? Survey, string? Refusal, IReadOnlyDictionary<string, string> Disc)> SurveyModAsync(string source)
    {
        var files = GameFilesFolder;
        return Task.Run(() =>
        {
            var disc = ModImport.DiscFiles(files);
            if (disc.Count == 0)
            {
                return (null, "No game files", disc);
            }
            var (survey, refusal) = ModImport.Survey(source, disc);
            return (survey, refusal, disc);
        });
    }

    /// <summary>The import, off the window's thread: the new mod goes to the end of the list, on (imported again, it
    /// keeps its place and switch, as does one of the first form imported from the same file, which it replaces).</summary>
    public Task<string?> ImportModAsync(ModSurvey survey, IReadOnlySet<string> on, IReadOnlyDictionary<string, string> disc,
        IProgress<ModProgress>? progress, CancellationToken cancel)
    {
        var files = GameFilesFolder;
        return Task.Run(() =>
        {
            Directory.CreateDirectory(ModLibrary.Folder);
            var (mod, refusal) = ModImport.Import(survey, on, files, disc, ModLibrary.Folder, progress, cancel);
            if (mod is null)
            {
                return refusal;
            }
            var installed = new InstalledMod(mod, Path.Combine(ModLibrary.Folder, mod.Id), true);
            bool Replaced(InstalledMod m) => m.Manifest.Id == mod.Id || (m.Manifest.Fixed && m.Manifest.SourceSha256 == mod.SourceSha256);
            var old = (Mods ?? []).Where(m => m.Manifest.Id != mod.Id && Replaced(m)).ToList();
            foreach (var m in old)
            {
                try
                {
                    ModLibrary.Remove(m);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Report("Mod not removed");
                }
            }
            ChangeMods(list => list.FirstOrDefault(Replaced) is { } first
                ? [.. list.Where(m => m == first || !Replaced(m)).Select(m => m == first ? installed with { On = m.On } : m)]
                : [.. list, installed]);
            return null;
        }, cancel);
    }

    /// <summary>A mod's car data made a stat pack with the launcher's import (<see cref="StatPackImport"/>), written
    /// into the mod's folder; the pack file's path, or why there is none.</summary>
    public Task<(string? Pack, string? Refusal)> StatPackOfModAsync(InstalledMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        var disc = Settings.DiscPath;
        return Task.Run<(string?, string?)>(() =>
        {
            if (mod.CarData is not { } carData)
            {
                return (null, "No car data");
            }
            var name = new string([.. mod.Manifest.Name.Where(c => c is >= ' ' and < (char)0x7F)]).Trim();
            name = name.Length is 0 ? "Mod" : name.Length > Online.Protocol.StatPack.MostName ? name[..Online.Protocol.StatPack.MostName] : name;
            var path = Path.Combine(mod.Folder, StatPackImport.FileNameOf(name));
            var lines = new StringWriter();
            var status = StatPackImport.Run(disc, carData, name, path, lines, overwrite: true);
            return status == StatPackImport.Written ? (path, null) : (null, lines.ToString().Split('\n')[0].Replace("refused: ", "", StringComparison.Ordinal).Trim());
        });
    }
}
