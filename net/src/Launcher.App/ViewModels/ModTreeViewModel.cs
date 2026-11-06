using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>One row of a mod's tree: a folder's name as the mod ships it (the top row: the mod's), its tick (all,
/// some or none of it; null: some) where it holds parts, and its arrow where it holds something; or one of the mod's
/// text files, which its arrow opens to read.</summary>
public sealed partial class ModTreeRow : ObservableObject
{
    private readonly ModTreeChoice _tree;

    internal ModTreeRow(ModTreeChoice tree, ModTreeNode node) => (_tree, Node) = (tree, node);

    public ModTreeNode Node { get; }

    public string Name => Node.Name;

    public bool HasChildren => Node.Children.Count > 0;

    /// <summary>The row has a tick: there are parts in it.</summary>
    public bool HasBox => Node.HasParts;

    /// <summary>A text file's row.</summary>
    public bool IsText => Node.Text is not null;

    /// <summary>A folder with nothing to tick in it (only text files).</summary>
    public bool IsPlainFolder => !IsText && !HasBox;

    /// <summary>The arrow: a folder with something in it, or a text file.</summary>
    public bool HasArrow => HasChildren || IsText;

    /// <summary>A text file's words, once it has been opened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsText))]
    public partial string? Words { get; set; }

    public bool ShowsText => IsText && Expanded && Words is not null;

    /// <summary>The mod's own row.</summary>
    public bool IsTop => Node.Depth == 0;

    /// <summary>The row's indent: its depth.</summary>
    public Thickness Indent => new(Node.Depth * 22, 0, 0, 0);

    public bool CanChange => _tree.Editable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsText))]
    public partial bool Expanded { get; set; }

    /// <summary>The tick: pressed, the row is all ticked, or none of it when it was all ticked.</summary>
    public bool? Checked
    {
        get => ModTree.TickOf(Node, _tree.On) switch
        {
            ModTick.On => true,
            ModTick.Off => false,
            _ => null,
        };
        set => _tree.Press(this);
    }

    internal void Ticked() => OnPropertyChanged(nameof(Checked));

    [RelayCommand]
    private void Expand() => _tree.Expand(this);
}

/// <summary>
/// A mod's parts as a tree to tick (<see cref="ModTree"/>): the import's chooser, and a card's tree, which says each
/// change to <c>changed</c> (the mods' list saves it at once; the next game start uses it). The top row and its own
/// folders show; a folder opens with its arrow, and a text file of the mod opens to its words (read by <c>read</c>,
/// off the window's thread, the first time). A tree that cannot change (a mod imported before the tree) shows its
/// ticks only.
/// </summary>
public sealed class ModTreeChoice : ObservableObject
{
    private readonly Action<IReadOnlySet<string>>? _changed;
    private readonly Func<string, string?>? _read;
    private readonly Dictionary<ModTreeNode, ModTreeRow> _rows = [];

    public ModTreeChoice(ModTree tree, IReadOnlySet<string> on, bool editable = true, Action<IReadOnlySet<string>>? changed = null,
        Func<string, string?>? read = null)
    {
        _read = read;
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(on);
        Tree = tree;
        On = new HashSet<string>(on, StringComparer.Ordinal);
        Editable = editable;
        _changed = changed;
        foreach (var node in tree.Rows)
        {
            _rows[node] = new ModTreeRow(this, node);
        }
        _rows[tree.Top].Expanded = true;
        Show();
    }

    public ModTree Tree { get; }

    /// <summary>The parts ticked.</summary>
    public HashSet<string> On { get; private set; }

    public bool Editable { get; }

    /// <summary>The rows shown: the top's, and those inside a row that is open.</summary>
    public ObservableCollection<ModTreeRow> Rows { get; } = [];

    /// <summary>Some part is ticked.</summary>
    public bool AnyOn => On.Count > 0;

    /// <summary>The row of a folder, by its place in the tree ("" the top).</summary>
    public ModTreeRow? Row(string key) => _rows.Values.FirstOrDefault(r => r.Node.Key == key);

    internal void Press(ModTreeRow row)
    {
        if (Editable)
        {
            On = ModTree.Press(row.Node, On);
            _changed?.Invoke(On);
        }
        foreach (var r in _rows.Values)
        {
            r.Ticked();
        }
        OnPropertyChanged(nameof(AnyOn));
    }

    /// <summary>The ticks as they are now (the mods' list changed them).</summary>
    public void Take(IReadOnlySet<string> on)
    {
        ArgumentNullException.ThrowIfNull(on);
        if (!On.SetEquals(on))
        {
            On = new HashSet<string>(on, StringComparer.Ordinal);
            foreach (var r in _rows.Values)
            {
                r.Ticked();
            }
            OnPropertyChanged(nameof(AnyOn));
        }
    }

    internal void Expand(ModTreeRow row)
    {
        if (row.IsText)
        {
            row.Expanded = !row.Expanded;
            if (row.Expanded && row.Words is null && _read is { } read && row.Node.Text is { } path)
            {
                _ = Task.Run(() => read(path)).ContinueWith(t => row.Words = t.IsCompletedSuccessfully ? t.Result ?? "" : "",
                    TaskScheduler.FromCurrentSynchronizationContext());
            }
            return;
        }
        if (!row.HasChildren)
        {
            return;
        }
        row.Expanded = !row.Expanded;
        Show();
    }

    /// <summary>Every text row that is open has its words.</summary>
    public bool TextsRead => _rows.Values.Where(r => r.IsText && r.Expanded).All(r => r.Words is not null);

    /// <summary>The rows shown made what they should be, the rows that stay kept as they are.</summary>
    private void Show()
    {
        var shown = new List<ModTreeRow>();
        void Add(ModTreeNode node)
        {
            var row = _rows[node];
            shown.Add(row);
            if (row.Expanded)
            {
                node.Children.ForEach(Add);
            }
        }
        Add(Tree.Top);
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!shown.Contains(Rows[i]))
            {
                Rows.RemoveAt(i);
            }
        }
        for (var i = 0; i < shown.Count; i++)
        {
            if (i >= Rows.Count || Rows[i] != shown[i])
            {
                Rows.Insert(i, shown[i]);
            }
        }
    }
}
