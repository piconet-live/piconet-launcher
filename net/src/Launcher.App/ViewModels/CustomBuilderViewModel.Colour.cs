using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

// The builder's colour, on the Garage's Colour tab as a stock machine's: the part chosen beside the wheel, its four stock
// colours (1 to 4, the first its stock colour 0) and the player's own paint (the wheel, the brightness bar, the hex
// box; once painted, a fifth swatch). The stock colours come from the
// colour model (ICustomColours); while it knows none, the swatches stand in grey and only the part's own colour and a
// paint can be chosen.
public sealed partial class CustomBuilderViewModel
{
    private const string Unknown = "#5A6478";
    private bool _showing;

    /// <summary>The parts a colour is chosen for.</summary>
    public static IReadOnlyList<string> PartTabs { get; } = ["Body", "Cockpit", "Booster"];

    /// <summary>The part whose colour is shown: 0 body, 1 cockpit, 2 booster.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PartTabIndex))]
    public partial int PartTab { get; set; }

    public int PartTabIndex
    {
        get => PartTab;
        set => PartTab = value < 0 ? PartTab : value;
    }

    /// <summary>The part's four stock colours and, when it has one, the player's paint.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StockSwatch> Swatches { get; set; } = [];

    /// <summary>The colour on the wheel and in the box: the paint, or the stock colour chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorValid))]
    public partial string ColorHex { get; set; } = "#FFFFFF";

    public bool ColorValid => Rgb.Parse(ColorHex) is not null;

    /// <summary>The 3D view's colours by part: a paint, a known stock colour, or null (the part's own).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Rgb?> PreviewColours { get; set; } = [null, null, null];

    partial void OnPartTabChanged(int value) => ShowColour();

    partial void OnColorHexChanged(string value)
    {
        if (_showing || Rgb.Parse(value) is not { } color)
        {
            return;
        }
        // a colour picked on the wheel or typed: the part painted
        Build = Build.WithColour(PartTab, new PartColour(null, color));
    }

    /// <summary>A swatch: stock colour 0-3, or -1 the part's paint.</summary>
    [RelayCommand]
    private void PickSwatch(int k)
    {
        var now = Build.ColourOf(PartTab);
        if (k >= 0)
        {
            Build = Build.WithColour(PartTab, now with { Stock = k });
        }
        else if (now.Paint is not null)
        {
            Build = Build.WithColour(PartTab, now with { Stock = null });
        }
    }

    /// <summary>The swatches, the wheel and the 3D view's colours for the build as it stands.</summary>
    private void ShowColour()
    {
        var part = Build.PartOf(PartTab);
        var colour = Build.ColourOf(PartTab);
        var stock = _colours.StockColours(part);
        var swatches = Enumerable.Range(0, 4).Select(k => new StockSwatch(k, stock is null ? Unknown : Rgb.Hex(stock[k]),
            $"{k + 1}", colour.Stock == k, stock is not null || k == 0)).ToList();
        if (colour.Paint is { } paint)
        {
            swatches.Add(new StockSwatch(-1, Rgb.Hex(paint), "Paint", colour.Stock is null, true));
        }
        Swatches = swatches;
        _showing = true;
        ColorHex = colour.Stock is null && colour.Paint is { } p ? Rgb.Hex(p)
            : stock is not null && colour.Stock is { } s ? Rgb.Hex(stock[s]) : ColorHex;
        _showing = false;
        PreviewColours = [.. Enumerable.Range(0, 3).Select(k => Shown(Build.PartOf(k), Build.ColourOf(k)))];
    }

    private Rgb? Shown(int part, PartColour colour) =>
        colour.Stock is null ? colour.Paint : _colours.StockColours(part) is { } stock ? stock[colour.Stock.Value] : null;
}
