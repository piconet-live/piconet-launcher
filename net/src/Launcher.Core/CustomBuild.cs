using Online.Protocol;

namespace Launcher.Core;

/// <summary>One part's colour as the builder keeps it: one of the part's four stock colours (0-3, 0 its own), or with
/// <see cref="Stock"/> null the player's <see cref="Paint"/>.</summary>
public sealed record PartColour(int? Stock = 0, Rgb? Paint = null)
{
    public static PartColour Own { get; } = new();
}

/// <summary>
/// A custom machine the player built in the Garage and saved: its three parts (by the game's ids: body 0-24, cockpit
/// 25-49, booster 50-74), its pilot (0-40, as the stock machine they drive) and each part's colour (body, cockpit,
/// booster; null: each part its own). Its name is never kept: it is the game's, from the three ids
/// (<see cref="CustomPartTables.NameOf"/>). <see cref="Favourite"/>: marked with the Garage's heart; it is no part of
/// the machine (two builds that differ only in it are the same machine).
/// </summary>
public sealed record CustomBuild(int Body, int Cockpit, int Booster, int Pilot, IReadOnlyList<PartColour>? Colours = null,
    bool Favourite = false)
{
    /// <summary>The builder's first machine: the first part of each kind, Captain Falcon.</summary>
    public static CustomBuild First { get; } = new(CustomMachine.Bodies, CustomMachine.Cockpits, CustomMachine.Boosters, 6);

    /// <summary>The game can build it: every id of its kind, a pilot there is, three colours or none.</summary>
    public bool Sound => AsRaced(NoColourWords.Instance).Problem() is null
        && Colours is null or { Count: 3 } && Colours?.All(c => c is not null && (c.Stock is >= 0 and <= 3 || (c.Stock is null && c.Paint is not null))) != false;

    /// <summary>A part's colour (0 body, 1 cockpit, 2 booster).</summary>
    public PartColour ColourOf(int part) => Colours is { Count: 3 } c && part is >= 0 and < 3 ? c[part] : PartColour.Own;

    /// <summary>The same machine with one part's colour changed.</summary>
    public CustomBuild WithColour(int part, PartColour colour)
    {
        var all = Enumerable.Range(0, 3).Select(ColourOf).ToArray();
        all[part] = colour;
        return this with { Colours = all };
    }

    /// <summary>The machine as a race carries it (<see cref="CustomMachine"/>), its colours in the race's words as
    /// <paramref name="colours"/> gives them; none when a part has no word (each part then its own colours).</summary>
    public CustomMachine AsRaced(ICustomColours colours)
    {
        ArgumentNullException.ThrowIfNull(colours);
        var words = Enumerable.Range(0, 3).Select(k => colours.Word(PartOf(k), ColourOf(k))).ToList();
        return new CustomMachine(Body, Cockpit, Booster, Pilot, words.All(w => w is not null) ? [.. words.Select(w => w!.Value)] : null);
    }

    /// <summary>The part id of slot 0 (body), 1 (cockpit), 2 (booster).</summary>
    public int PartOf(int slot) => slot switch
    {
        0 => Body,
        1 => Cockpit,
        _ => Booster,
    };

    /// <summary>Two builds are the same machine: the same parts, pilot and colours.</summary>
    public bool Equals(CustomBuild? other) =>
        other is not null && Body == other.Body && Cockpit == other.Cockpit && Booster == other.Booster && Pilot == other.Pilot
        && Enumerable.Range(0, 3).All(k => ColourOf(k) == other.ColourOf(k));

    public override int GetHashCode() => HashCode.Combine(Body, Cockpit, Booster, Pilot);
}

/// <summary>
/// How a custom part is coloured: its four stock colours, and the word a race carries for a colour of it (the
/// garage record's part entry +4). The builder is written against this
/// interface only: <see cref="PartColourWords"/> is the game's rule, with the stock colours read from the player's
/// disc (<see cref="PartColours"/>); <see cref="NoColourWords"/> knows nothing.
/// </summary>
public interface ICustomColours
{
    /// <summary>A part's four stock colours (0 its own) as swatches; null while they are not known.</summary>
    IReadOnlyList<Rgb>? StockColours(int part);

    /// <summary>The race's colour word for a part in this colour; null: the part races in its own colours.</summary>
    uint? Word(int part, PartColour colour);
}

/// <summary>The stand-in until the colour model is read: nothing known, every part in its own colours.</summary>
public sealed class NoColourWords : ICustomColours
{
    public static NoColourWords Instance { get; } = new();

    public IReadOnlyList<Rgb>? StockColours(int part) => null;

    public uint? Word(int part, PartColour colour) => null;
}

/// <summary>
/// The game's colour words: red &lt;&lt; 24 | green &lt;&lt; 16 |
/// blue &lt;&lt; 8 | slot, the slot always 1 (any other low byte paints nothing); 0 is the part's stock colour 0. Only
/// 0 and slot-1 words are ever made. A part's four stock colours come from <paramref name="stockWords"/> (four words a
/// part, stock colour 0 first; null while not known): without them, stock colours 1 to 3 have no word.
/// </summary>
public sealed class PartColourWords(Func<int, IReadOnlyList<uint>?> stockWords) : ICustomColours
{
    public IReadOnlyList<Rgb>? StockColours(int part) =>
        stockWords(part) is { Count: 4 } words ? [.. words.Select(w => new Rgb((byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8)))] : null;

    public uint? Word(int part, PartColour colour)
    {
        ArgumentNullException.ThrowIfNull(colour);
        return colour switch
        {
            { Stock: 0 } => 0,
            { Stock: >= 1 and <= 3 and var k } => stockWords(part) is { Count: 4 } words ? (words[k] & 0xFFFFFF00) | 1 : null,
            { Stock: null, Paint: { } p } => ((uint)p.R << 24) | ((uint)p.G << 16) | ((uint)p.B << 8) | 1,
            _ => null,
        };
    }
}
