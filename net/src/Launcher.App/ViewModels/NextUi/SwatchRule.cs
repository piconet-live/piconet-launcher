using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// An option row that picks a colour (the redesigned Garage's builder: a part's colour, the trail's): left and right go
/// through the colours it has (a part's or a machine's stock colours, then the player's own paint once there is one),
/// each shown as a swatch beside its word; A opens every colour (the colours panel: the hues, a colour typed, the
/// wheel). What a colour picked does is its owner's (<paramref name="pick"/>: the swatch's index, 0-3 a stock colour,
/// -1 the paint).
/// </summary>
public sealed partial class SwatchRule(string label, Action<int> pick, Action open) : RuleRow(label)
{
    /// <summary>The colours to step through (only the ones known: a stock colour not read from the disc is left out).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chosen), nameof(Hex))]
    public partial IReadOnlyList<StockSwatch> Swatches { get; private set; } = [];

    /// <summary>Which of <see cref="Swatches"/> is chosen; -1 none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chosen), nameof(Hex))]
    public partial int Selected { get; private set; } = -1;

    /// <summary>The colour chosen in words: "Its own", "Colour 2", "Your paint".</summary>
    public string Chosen => Selected >= 0 && Selected < Swatches.Count ? Words(Swatches[Selected]) : "";

    /// <summary>The colour chosen as "#RRGGBB" (the row's swatch); null none.</summary>
    public string? Hex => Selected >= 0 && Selected < Swatches.Count ? Swatches[Selected].Hex : null;

    /// <summary>The colours as they stand now, the one chosen marked (<see cref="StockSwatch.Selected"/>).</summary>
    public void Show(IEnumerable<StockSwatch> swatches)
    {
        ArgumentNullException.ThrowIfNull(swatches);
        var known = swatches.Where(s => s.Known).ToList();
        if (!known.SequenceEqual(Swatches))
        {
            Swatches = known;
        }
        Selected = known.FindIndex(s => s.Selected);
    }

    /// <summary>Left (-1) or right (1): the colour before or after; false at either end.</summary>
    public bool Step(int by)
    {
        var to = Math.Clamp((Selected < 0 ? 0 : Selected) + by, 0, Swatches.Count - 1);
        if (Swatches.Count == 0 || to == Selected)
        {
            return false;
        }
        pick(Swatches[to].Index);
        return true;
    }

    /// <summary>A: every colour, in the colours panel.</summary>
    public void Open() => open();

    /// <summary>A swatch in words: its own (the first stock colour), a stock colour by its number, the paint.</summary>
    public static string Words(StockSwatch swatch)
    {
        ArgumentNullException.ThrowIfNull(swatch);
        return swatch.Index switch
        {
            < 0 => "Your paint",
            0 => "Its own",
            _ => $"Colour {swatch.Index + 1}",
        };
    }
}
