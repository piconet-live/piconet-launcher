using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.App.ViewModels;

// The column beside the 3D view keeps one row of tabs whatever the shelf or the machine (tabs
// that move about confuse): Body, Cockpit, Booster, Pilot, Colour, Stats. A part's tab shows that
// part's list (the builder's, on the Custom shelf); a stock machine has no parts, and its four are there, dim. Under
// the column's content one row of actions, Save, Remove, New machine, in the same place on every shelf: on Favourites
// and Stock only New machine is pressed (a stock machine's look is kept as it is made).
public sealed partial class GarageViewModel
{
    public const int ColourSection = 4, StatsSection = 5;

    /// <summary>The column's tabs, in their one order.</summary>
    public IReadOnlyList<SectionTab> Sections { get; } =
        [new("Body", 0), new("Cockpit", 1), new("Booster", 2), new("Pilot", 3), new("Colour", ColourSection), new("Stats", StatsSection)];

    /// <summary>The tab last chosen. A part's tab is a custom machine's only: on Favourites and Stock the column then
    /// shows the colour, and the part's tab is there again on the Custom shelf.</summary>
    [ObservableProperty]
    public partial int Section { get; set; } = ColourSection;

    /// <summary>The tab the column shows.</summary>
    public int ShownSection => CustomShown || Section >= ColourSection ? Section : ColourSection;

    /// <summary>A stock machine's column: 0 its colour, 1 its stats (the page's older switch, which tests and the
    /// lobby's tests still set).</summary>
    public int Panel
    {
        get => ShownSection == StatsSection ? 1 : 0;
        set => Section = value == 1 ? StatsSection : ColourSection;
    }

    /// <summary>A stock machine's colour (Favourites, Stock).</summary>
    public bool ColourShown => StockShown && ShownSection == ColourSection;

    /// <summary>A stock machine's stats (Favourites, Stock).</summary>
    public bool StatsShown => StockShown && ShownSection == StatsSection;

    /// <summary>The custom machine's part colours (Custom).</summary>
    public bool BuildColourShown => CustomShown && ShownSection == ColourSection;

    /// <summary>The custom machine's stats (Custom).</summary>
    public bool BuildStatsShown => CustomShown && ShownSection == StatsSection;

    /// <summary>A list of parts or pilots (Custom).</summary>
    public bool PartsShown => CustomShown && ShownSection < ColourSection;

    /// <summary>The list a part's tab shows; null on the others.</summary>
    public PartColumn? ShownParts => PartsShown ? Builder.Columns[ShownSection] : null;

    /// <summary>The 3D view's custom machine: the build on the Custom shelf; null, the stock machine.</summary>
    public Launcher.Core.CustomBuild? ShownBuild => CustomShown ? Builder.Build : null;

    /// <summary>Remove takes the open saved machine away: on the Custom shelf, a saved machine open.</summary>
    public bool CanRemove => CustomShown && Builder.Editing is not null;

    partial void OnSectionChanged(int value) => ShowSections();

    /// <summary>A tab pressed (never a part's on Favourites and Stock: those are dim).</summary>
    [RelayCommand]
    private void ShowSection(SectionTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.Enabled)
        {
            Section = tab.Index;
        }
    }

    /// <summary>The tabs' marks and what the column and the 3D view show, after a tab or a shelf.</summary>
    private void ShowSections()
    {
        foreach (var tab in Sections)
        {
            tab.Enabled = tab.Index >= ColourSection || CustomShown;
            tab.On = tab.Index == ShownSection;
        }
        foreach (var name in (string[])[nameof(ShownSection), nameof(Panel), nameof(ColourShown), nameof(StatsShown), nameof(BuildColourShown),
            nameof(BuildStatsShown), nameof(PartsShown), nameof(ShownParts), nameof(ShownBuild), nameof(CanRemove)])
        {
            OnPropertyChanged(name);
        }
    }
}

/// <summary>A tab of the column beside the 3D view: its word, its place, whether it shows (On) and whether it can be
/// pressed (a part's tab for a stock machine cannot).</summary>
public sealed partial class SectionTab(string label, int index) : ObservableObject
{
    public string Label { get; } = label;

    public int Index { get; } = index;

    [ObservableProperty]
    public partial bool On { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;
}
