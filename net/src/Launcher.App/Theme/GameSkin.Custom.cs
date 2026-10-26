using Launcher.Core;

namespace Launcher.App.Theme;

// The custom machines' part names and stats from the player's disc, read once a disc on a worker thread, as the
// stock machines' are: nothing of the game is in the launcher.
public static partial class GameSkin
{
    private static DiscArt? _customsOf;
    private static Task<CustomParts?> _customs = Task.FromResult<CustomParts?>(null);

    private static readonly Dictionary<(DiscArt Art, int Body, int Cockpit, int Booster), Task<GarageMachine?>> Customs3D = [];

    /// <summary>A custom machine's model for the builder's preview, read once on a worker thread (null without the
    /// disc or when its files cannot be read).</summary>
    public static Task<GarageMachine?> Custom3D(int body, int cockpit, int booster)
    {
        if (_art is not { } art)
        {
            return Task.FromResult<GarageMachine?>(null);
        }
        lock (Gate)
        {
        if (!Customs3D.TryGetValue((art, body, cockpit, booster), out var task))
        {
            task = Task.Run(async () =>
            {
                var file = await MachinesFolderAsync() is { } folder ? MachineCache.CustomFile(folder, body, cockpit, booster) : null;
                using var job = LoadTrace.Begin($"custom {body}/{cockpit}/{booster} model");
                if (Kept(file) is { Paint: null, Anchors: { } anchors } cached)
                {
                    return new GarageMachine(cached.Model, null, anchors);
                }
                var loaded = Quietly(() => art.CustomModel(body, cockpit, booster) is { } m ? new GarageMachine(m.Model, null, m.Anchors) : null);
                Keep(file, loaded);
                return loaded;
            });
            Customs3D[(art, body, cockpit, booster)] = task;
        }
        return task;
        }
    }

    /// <summary>Forgets the custom models read (under <see cref="Gate"/>: the disc changed).</summary>
    private static void ClearCustoms3D() => Customs3D.Clear();

    /// <summary>A raced machine's name as every page writes it (<see cref="RacedMachine.Name"/>): a custom machine's by
    /// the game's rule from the disc's tables, a stock machine's from the list. While the tables are being read a
    /// custom machine is <see cref="RacedMachine.UnnamedCustom"/>, never its stock machine, and
    /// <paramref name="whenRead"/> runs (on a worker thread) once they are.</summary>
    public static string NameOf(RacedMachine raced, Action? whenRead = null)
    {
        ArgumentNullException.ThrowIfNull(raced);
        if (!raced.IsCustom)
        {
            return raced.Name(null);
        }
        var reading = CustomPartsAsync();
        if (!reading.IsCompleted && whenRead is not null)
        {
            _ = reading.ContinueWith(_ => whenRead(), TaskScheduler.Default);
        }
        return raced.Name(reading is { IsCompletedSuccessfully: true, Result: { } parts } ? parts.Tables : null);
    }

    /// <summary>The custom parts' naming tables and the car data their stats are summed from; null without the disc,
    /// or when its main module is not the pinned one (the tables' places are that file's).</summary>
    public static Task<CustomParts?> CustomPartsAsync()
    {
        var art = _art;
        if (!ReferenceEquals(art, _customsOf))
        {
            _customsOf = art;
            _customs = art is null ? Task.FromResult<CustomParts?>(null)
                : Task.Run(() => Quietly(() => art.CustomParts() is { } tables ? new CustomParts(tables, art.CarData(), art.PartColours(), art.CustomGrades()) : null));
        }
        return _customs;
    }
}

/// <summary>What the builder knows of the parts: their names (and the machines' names, by the game's rule), the car
/// data their stats are summed from (null when the disc's could not be read), each part's four stock colours as the
/// game's words (<see cref="Launcher.Core.PartColours"/>; null when not read), and each part's letter
/// (<see cref="CustomPartFile.Grades"/>; null when not read).</summary>
public sealed class CustomParts(CustomPartTables tables, byte[]? carData, uint[][]? colours = null, int[]? grades = null)
{
    private (string Fingerprint, byte[] Data)? _packed;   // the car data with the last pack asked for over it

    public CustomPartTables Tables { get; } = tables;

    /// <summary>A part's four stock colour words, stock colour 0 first; null when not known.</summary>
    public IReadOnlyList<uint>? StockWords(int part) => colours is not null && part >= 0 && part < colours.Length ? colours[part] : null;

    /// <summary>The machine's summed stats by <see cref="MachineStat"/>, its three letters with them; with a stat
    /// pack, summed from the parts as the pack leaves them, as a race of that pack sums them. Null without the car
    /// data.</summary>
    public float?[]? Stats(CustomBuild build, Online.Protocol.StatPack? pack = null)
    {
        if (build is null || carData is null)
        {
            return null;
        }
        var data = carData;
        if (pack is not null)
        {
            if (_packed?.Fingerprint != pack.Fingerprint)
            {
                _packed = (pack.Fingerprint, CustomPartFile.WithPack(carData, pack));
            }
            data = _packed.Value.Data;
        }
        var row = CustomPartFile.Sum(data, build.Body, build.Cockpit, build.Booster);
        CustomPartFile.Rate(row, grades, build.Body, build.Cockpit, build.Booster);
        return row;
    }
}
