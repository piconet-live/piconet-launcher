using Online.Protocol;

namespace Launcher.Core;

/// <summary>A custom machine's part kinds, by the game's part ids: bodies 0-24, cockpits 25-49, boosters 50-74
/// (<see cref="CustomMachine"/>).</summary>
public enum PartKind
{
    Body,
    Cockpit,
    Booster,
}

/// <summary>
/// The custom machines' part names and the game's rule that names a machine from its three parts
/// from tables read out of the player's own main module
/// (<see cref="CustomPartFile.Tables"/>): nothing of the game is in the launcher. A machine's name is never typed:
/// it is the game's, from the three ids.
/// </summary>
public sealed class CustomPartTables
{
    /// <summary>Parts of each kind.</summary>
    public const int PerKind = 25;

    private readonly IReadOnlyList<string> _parts;
    private readonly IReadOnlyList<uint> _flags;
    private readonly int[] _listedKeys;
    private readonly IReadOnlyList<string> _listedNames;

    /// <param name="parts">The 75 parts' English strings, by id: a body or cockpit's four words (its first name, then
    /// its second: "Brave Eagle Strike Condor"), a booster's model and suffix ("Triangle -GT -GT").</param>
    /// <param name="flags">The 75 words whose low five bits are a booster's naming flags, by part id.</param>
    /// <param name="listedKeys">The named combinations' keys, rising.</param>
    /// <param name="listedNames">Their names, in the same order.</param>
    public CustomPartTables(IReadOnlyList<string> parts, IReadOnlyList<uint> flags, IReadOnlyList<int> listedKeys,
        IReadOnlyList<string> listedNames)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(listedKeys);
        ArgumentNullException.ThrowIfNull(listedNames);
        if (parts.Count != CustomMachine.Parts || flags.Count != CustomMachine.Parts || listedKeys.Count != listedNames.Count)
        {
            throw new ArgumentException("75 parts and flags, and a name for every listed key.");
        }
        (_parts, _flags, _listedKeys, _listedNames) = (parts, flags, [.. listedKeys], listedNames);
    }

    /// <summary>The kind of a part id; null for no part.</summary>
    public static PartKind? KindOf(int part) => part switch
    {
        >= CustomMachine.Bodies and < CustomMachine.Cockpits => PartKind.Body,
        >= CustomMachine.Cockpits and < CustomMachine.Boosters => PartKind.Cockpit,
        >= CustomMachine.Boosters and < CustomMachine.Parts => PartKind.Booster,
        _ => null,
    };

    /// <summary>The first part id of a kind.</summary>
    public static int First(PartKind kind) => (int)kind * PerKind;

    /// <summary>A part's name as the game's parts list shows it: a body or cockpit's first name ("Dread Hammer"), a
    /// booster's model ("Titan"); "" for no part.</summary>
    public string PartName(int part)
    {
        if (KindOf(part) is not { } kind)
        {
            return "";
        }
        var words = Words(_parts[part]);
        return kind == PartKind.Booster ? Word(words, 0) : $"{Word(words, 0)} {Word(words, 1)}".Trim();
    }

    /// <summary>The name the game gives the machine of these three parts (English, as a race shows it); "" when an id
    /// is not of its kind (the game does not check: the launcher never asks).</summary>
    public string NameOf(int body, int cockpit, int booster)
    {
        if (KindOf(body) != PartKind.Body || KindOf(cockpit) != PartKind.Cockpit || KindOf(booster) != PartKind.Booster)
        {
            return "";
        }
        var key = (body << 10) | ((cockpit - CustomMachine.Cockpits) << 5) | (booster - CustomMachine.Boosters);
        var listed = Array.BinarySearch(_listedKeys, key);
        if (listed >= 0)
        {
            return _listedNames[listed];
        }
        var flags = _flags[booster];
        var b = Words(_parts[body]);
        var c = Words(_parts[cockpit]);
        // flag 0x08 the body's second name, 0x10 the cockpit's; 0x01 the cockpit first; 0x02 the booster's suffix
        var (b0, b1) = (flags & 0x08) != 0 ? (Word(b, 2), Word(b, 3)) : (Word(b, 0), Word(b, 1));
        var (c0, c1) = (flags & 0x10) != 0 ? (Word(c, 2), Word(c, 3)) : (Word(c, 0), Word(c, 1));
        var (first, second) = (flags & 0x01) != 0 ? (c0, b1) : (b0, c1);
        var suffix = (flags & 0x02) != 0 ? Word(Words(_parts[booster]), 2) : "";
        return $"{first} {second}{suffix}";
    }

    private static string[] Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Word(string[] words, int n) => n < words.Length ? words[n] : "";
}

/// <summary>The 41 pilots a custom machine can carry, by the stock machine they drive (the game keeps the pilot as
/// that machine's id).</summary>
public static class Pilots
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Mighty Gazelle", "Jody Summer", "Dr. Stewart", "Baba", "Samurai Goroh", "Pico", "Captain Falcon", "Octoman",
        "Mr. EAD", "James McCloud", "Billy", "Kate Alen", "Zoda", "Jack Levin", "Bio Rex", "The Skull", "Antonio Guster",
        "Beastman", "Leon", "Super Arrow", "Mrs. Arrow", "Gomar & Shioh", "Silver Neelsen", "Michael Chain",
        "Blood Falcon", "John Tanaka", "Draq", "Roger Buster", "Dr. Clash", "Black Shadow", "Deathborn", "Don Genie",
        "Digi-Boy", "Dai San Gen", "Spade", "Daigoroh", "Princia Ramode", "Lily Flyer", "PJ", "QQQ", "Phoenix",
    ];

    public static string NameOf(int pilot) => pilot >= 0 && pilot < Names.Count ? Names[pilot] : "";
}
