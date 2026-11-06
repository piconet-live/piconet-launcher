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
/// folder itself (<see cref="Own"/>: a folder laid out like the disc), and what is in it in Explorer's order, folders
/// first, then the mod's own text files (a row of <see cref="Text"/>: a readme, the author's notes) to read.</summary>
public sealed class ModTreeNode
{
    internal ModTreeNode(string name, string key, ModTreeNode? parent, string? text = null)
    {
        (Name, Key, Parent, Text) = (name, key, parent, text);
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    public string Name { get; }

    /// <summary>The row's place in the tree, its folders' names joined by '/' ("" the top row).</summary>
    public string Key { get; }

    public ModTreeNode? Parent { get; }

    public int Depth { get; }

    /// <summary>A text file's row: its path in the mod. Null: a folder's.</summary>
    public string? Text { get; }

    public List<string> Own { get; } = [];

    public List<ModTreeNode> Children { get; } = [];

    /// <summary>Every part in this row and below it.</summary>
    public IEnumerable<string> Parts => Own.Concat(Children.SelectMany(c => c.Parts));

    /// <summary>Something in this row or below it can be ticked.</summary>
    public bool HasParts => Parts.Any();
}

/// <summary>
/// A mod's own folders as a tree to tick: the top row is the mod (all of it), below it the folders between the
/// archive's top and its parts (<see cref="ModImport.Survey"/>'s roots: folders laid out like the disc), each part a
/// row. The tree stops at the parts: the game's folders inside a part are not in it. A part's own last folder named
/// "root" is not a row of its own (GXtreme's "_XTREMEv5/root" is the row "_XTREMEv5"), and the folders every part
/// sits in are the top row. A folder's tick is all, some or none of what is in it; any mix may be ticked, and the
/// ticked parts are laid in Explorer's order, a later over an earlier, as copying the folders by hand. The mod's own
/// text files are rows where they sit, with the folders that hold only them (nothing to tick there).
/// </summary>
public sealed class ModTree
{
    private ModTree(ModTreeNode top, IReadOnlyList<string> parts) => (Top, Parts) = (top, parts);

    public ModTreeNode Top { get; }

    /// <summary>The parts in Explorer's order: the order a later one's files win in.</summary>
    public IReadOnlyList<string> Parts { get; }

    /// <summary>The rows, top first, each before the rows in it.</summary>
    public IEnumerable<ModTreeNode> Rows => Walk(Top);

    private static IEnumerable<ModTreeNode> Walk(ModTreeNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));

    /// <summary>The tree of a mod named <paramref name="name"/> with these parts (paths in the mod, "" its top) and
    /// text files (paths in the mod).</summary>
    public static ModTree Of(string name, IEnumerable<string> parts, IEnumerable<string>? texts = null)
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
        // each folder of the mod that is a row, by its path in the mod; and the parts' own folders
        var rows = new Dictionary<string, ModTreeNode>(StringComparer.OrdinalIgnoreCase);
        var inPart = new HashSet<ModTreeNode>();
        if (shown.Count > 0)
        {
            for (var d = 0; d <= common; d++)
            {
                rows[string.Join('/', shown[0].Take(d))] = top;
            }
        }
        for (var p = 0; p < list.Count; p++)
        {
            var node = top;
            var folders = shown[p];
            for (var d = common; d < folders.Length; d++)
            {
                node = Child(node, folders[d]);
                rows[string.Join('/', folders.Take(d + 1))] = node;
            }
            node.Own.Add(list[p]);
            rows[list[p]] = node;
            inPart.Add(node);
        }
        foreach (var text in (texts ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var folders = text.Split('/');
            // the deepest folder of the text that is a row, then its folders below that one (none inside a part)
            var at = folders.Length - 1;
            while (at > 0 && !rows.ContainsKey(string.Join('/', folders.Take(at))))
            {
                at--;
            }
            var node = rows.TryGetValue(string.Join('/', folders.Take(at)), out var known) ? known : top;
            for (var d = at; d < folders.Length - 1 && !inPart.Contains(node); d++)
            {
                node = Child(node, folders[d]);
                rows[string.Join('/', folders.Take(d + 1))] = node;
            }
            node.Children.Add(new ModTreeNode(folders[^1], node.Key.Length == 0 ? folders[^1] : node.Key + "/" + folders[^1], node, text));
        }
        foreach (var node in Walk(top))
        {
            node.Children.Sort((a, b) => a.Text is null != b.Text is null ? (a.Text is null ? -1 : 1) : ExplorerOrder.Names.Compare(a.Name, b.Name));
        }
        return new ModTree(top, list);
    }

    private static ModTreeNode Child(ModTreeNode node, string folder)
    {
        var next = node.Children.Find(c => c.Text is null && c.Name.Equals(folder, StringComparison.OrdinalIgnoreCase));
        if (next is null)
        {
            next = new ModTreeNode(folder, node.Key.Length == 0 ? folder : node.Key + "/" + folder, node);
            node.Children.Add(next);
        }
        return next;
    }

    /// <summary>A part's folders as rows: its last folder left out when it is named "root".</summary>
    private static string[] Shown(string part)
    {
        var folders = part.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return folders.Length > 0 && folders[^1].Equals("root", StringComparison.OrdinalIgnoreCase) ? folders[..^1] : folders;
    }

    /// <summary>How much of a row is ticked (of its parts and the rows in it that have some).</summary>
    public static ModTick TickOf(ModTreeNode node, IReadOnlySet<string> on)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(on);
        var parts = node.Parts.ToList();
        var ticked = parts.Count(on.Contains);
        return ticked == 0 ? ModTick.Off : ticked == parts.Count ? ModTick.On : ModTick.Some;
    }

    /// <summary>A row pressed: all of it ticked, or, when it is all ticked already, none of it.</summary>
    public static HashSet<string> Press(ModTreeNode node, IReadOnlySet<string> on)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(on);
        var now = new HashSet<string>(on, StringComparer.Ordinal);
        if (TickOf(node, on) == ModTick.On)
        {
            now.ExceptWith(node.Parts);
        }
        else
        {
            now.UnionWith(node.Parts);
        }
        return now;
    }
}
