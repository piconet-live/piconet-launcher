using System.Text.RegularExpressions;

namespace Launcher.Core;

/// <summary>
/// Names in the order Windows Explorer lists them (as <c>StrCmpLogicalW</c>): letters without regard to case, a run of
/// digits by its number ("Step 2" before "Step 10"), and a sign or a space before a digit, a digit before a letter
/// ("_XTREMEv5" before "Japanese fixes"). A mod's author writes "copy this, then these over it" in that order, so a
/// later part's file wins over an earlier one's (<see cref="ModManifest.Files"/>).
/// </summary>
public sealed class ExplorerOrder : IComparer<string>
{
    /// <summary>One folder's or file's name.</summary>
    public static ExplorerOrder Names { get; } = new();

    /// <summary>A path (forward slashes) folder by folder, a folder before what is in it.</summary>
    public static IComparer<string> Paths { get; } = Comparer<string>.Create(ComparePaths);

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var (a, b) = (Run(x, ref i), Run(y, ref j));
                var by = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (by != 0)
                {
                    return by;
                }
                continue;
            }
            var (ra, rb) = (Rank(x[i]), Rank(y[j]));
            if (ra != rb)
            {
                return ra.CompareTo(rb);
            }
            var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
            if (c != 0)
            {
                return c;
            }
            i++;
            j++;
        }
        var left = (x.Length - i).CompareTo(y.Length - j);
        return left != 0 ? left : string.CompareOrdinal(x, y);
    }

    /// <summary>A run of digits from <paramref name="at"/>, its leading zeros left out; <paramref name="at"/> moved past it.</summary>
    private static string Run(string s, ref int at)
    {
        var start = at;
        while (at < s.Length && char.IsAsciiDigit(s[at]))
        {
            at++;
        }
        var run = s[start..at].TrimStart('0');
        return run.Length == 0 ? "0" : run;
    }

    /// <summary>A sign or a space first, then a digit, then a letter.</summary>
    private static int Rank(char c) => char.IsAsciiDigit(c) ? 1 : char.IsLetter(c) ? 2 : 0;

    private static int ComparePaths(string? x, string? y)
    {
        var a = (x ?? "").Split('/');
        var b = (y ?? "").Split('/');
        for (var k = 0; k < Math.Min(a.Length, b.Length); k++)
        {
            // folders of one name in another case are one folder (as on Windows)
            if (!a[k].Equals(b[k], StringComparison.OrdinalIgnoreCase))
            {
                return Names.Compare(a[k], b[k]);
            }
        }
        var by = a.Length.CompareTo(b.Length);
        return by != 0 ? by : string.CompareOrdinal(x, y);
    }
}

/// <summary>How much of a folder of the tree is ticked.</summary>
public enum ModTick
{
    Off,
    Some,
    On,
}

/// <summary>One row of a mod's tree: a folder as the mod ships it (the top row: the mod), the parts that are this
/// folder itself (<see cref="Own"/>: a folder laid out like the disc), and the folders in it in Explorer's order.
/// <see cref="Group"/>: the number of a lettered alternative ("2A", "Step 3B") that has siblings of the same number,
/// of which one at most is ticked.</summary>
public sealed class ModTreeNode
{
    internal ModTreeNode(string name, string key, ModTreeNode? parent)
    {
        (Name, Key, Parent) = (name, key, parent);
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    public string Name { get; }

    /// <summary>The row's place in the tree, its folders' names joined by '/' ("" the top row).</summary>
    public string Key { get; }

    public ModTreeNode? Parent { get; }

    public int Depth { get; }

    public List<string> Own { get; } = [];

    public List<ModTreeNode> Children { get; } = [];

    public string? Group { get; internal set; }

    /// <summary>Every part in this row and below it.</summary>
    public IEnumerable<string> Parts => Own.Concat(Children.SelectMany(c => c.Parts));
}

/// <summary>
/// A mod's own folders as a tree to tick: the top row is the mod (all of it), below it the folders between the
/// archive's top and its parts (<see cref="ModImport.Survey"/>'s roots: folders laid out like the disc), each part a
/// row. The tree stops at the parts: the game's folders inside a part are not in it. A part's own last folder named
/// "root" is not a row of its own (GXtreme's "_XTREMEv5/root" is the row "_XTREMEv5"), and the folders every part
/// sits in are the top row. A folder's tick is all, some or none of what is in it; a lettered alternative is one of
/// its siblings: ticking one unticks the others, and ticking the folder they are in ticks one of them.
/// </summary>
public sealed class ModTree
{
    private static readonly Regex Variant = new(@"^(?:step\s*)?(\d+)([a-z])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private ModTree(ModTreeNode top, IReadOnlyList<string> parts) => (Top, Parts) = (top, parts);

    public ModTreeNode Top { get; }

    /// <summary>The parts in Explorer's order: the order a later one's files win in.</summary>
    public IReadOnlyList<string> Parts { get; }

    /// <summary>The rows, top first, each before the rows in it.</summary>
    public IEnumerable<ModTreeNode> Rows => Walk(Top);

    private static IEnumerable<ModTreeNode> Walk(ModTreeNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));

    /// <summary>The tree of a mod named <paramref name="name"/> with these parts (paths in the mod, "" its top).</summary>
    public static ModTree Of(string name, IEnumerable<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var list = parts.Distinct(StringComparer.Ordinal).Order(ExplorerOrder.Paths).ToList();
        var shown = list.Select(Shown).ToList();
        // the folders every part sits in: the top row
        var common = 0;
        while (shown.Count > 0 && shown.All(s => s.Length > common) && shown.All(s => s[common].Equals(shown[0][common], StringComparison.OrdinalIgnoreCase)))
        {
            common++;
        }
        var top = new ModTreeNode(name, "", null);
        for (var p = 0; p < list.Count; p++)
        {
            var node = top;
            foreach (var folder in shown[p].Skip(common))
            {
                var next = node.Children.Find(c => c.Name.Equals(folder, StringComparison.OrdinalIgnoreCase));
                if (next is null)
                {
                    next = new ModTreeNode(folder, node.Key.Length == 0 ? folder : node.Key + "/" + folder, node);
                    node.Children.Add(next);
                }
                node = next;
            }
            node.Own.Add(list[p]);
        }
        foreach (var node in Walk(top))
        {
            node.Children.Sort((a, b) => ExplorerOrder.Names.Compare(a.Name, b.Name));
            var numbers = node.Children.Select(c => Variant.Match(c.Name)).Where(m => m.Success).GroupBy(m => int.Parse(m.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            foreach (var child in node.Children)
            {
                if (Variant.Match(child.Name) is { Success: true } m && numbers.Contains(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)))
                {
                    child.Group = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        return new ModTree(top, list);
    }

    /// <summary>A part's folders as rows: its last folder left out when it is named "root".</summary>
    private static string[] Shown(string part)
    {
        var folders = part.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return folders.Length > 0 && folders[^1].Equals("root", StringComparison.OrdinalIgnoreCase) ? folders[..^1] : folders;
    }

    /// <summary>How much of a row is ticked: a group of alternatives counts as one, ticked when one of them is.</summary>
    public static ModTick TickOf(ModTreeNode node, IReadOnlySet<string> on)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(on);
        var ticks = node.Own.Select(p => on.Contains(p) ? ModTick.On : ModTick.Off).ToList();
        foreach (var slot in Slots(node))
        {
            if (slot.Count == 1)
            {
                ticks.Add(TickOf(slot[0], on));
                continue;
            }
            var members = slot.Select(c => TickOf(c, on)).Where(t => t != ModTick.Off).ToList();
            ticks.Add(members.Count == 0 ? ModTick.Off : members is [ModTick.On] ? ModTick.On : ModTick.Some);
        }
        return ticks.Count == 0 || ticks.All(t => t == ModTick.Off) ? ModTick.Off : ticks.All(t => t == ModTick.On) ? ModTick.On : ModTick.Some;
    }

    /// <summary>A row's children as the tick counts them: one alone, or a group of alternatives.</summary>
    private static IEnumerable<List<ModTreeNode>> Slots(ModTreeNode node) =>
        node.Children.GroupBy(c => c.Group ?? "\0" + c.Key).Select(g => g.ToList());

    /// <summary>A row pressed: all of it ticked, or, when it is all ticked already, none of it. Ticking an
    /// alternative unticks its siblings.</summary>
    public static HashSet<string> Press(ModTreeNode node, IReadOnlySet<string> on)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(on);
        var now = new HashSet<string>(on, StringComparer.Ordinal);
        var tick = TickOf(node, on) != ModTick.On;
        if (tick && node.Group is { } group && node.Parent is { } parent)
        {
            foreach (var sibling in parent.Children.Where(c => c != node && c.Group == group))
            {
                now.ExceptWith(sibling.Parts);
            }
        }
        Set(node, tick, now);
        return now;
    }

    /// <summary>All of a row ticked (in each group of alternatives the one ticked already, else the first) or none.</summary>
    private static void Set(ModTreeNode node, bool tick, HashSet<string> on)
    {
        if (!tick)
        {
            on.ExceptWith(node.Parts);
            return;
        }
        on.UnionWith(node.Own);
        foreach (var slot in Slots(node))
        {
            var pick = slot.Count == 1 ? slot[0] : slot.Find(c => TickOf(c, on) != ModTick.Off) ?? slot[0];
            foreach (var other in slot.Where(c => c != pick))
            {
                on.ExceptWith(other.Parts);
            }
            Set(pick, true, on);
        }
    }
}
