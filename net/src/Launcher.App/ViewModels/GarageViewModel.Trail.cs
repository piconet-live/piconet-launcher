using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

// The trail's one colour, on the Colour tab as a fourth beside Body, Cockpit and Booster (a stock machine's: beside
// Machine), with the same picker: the machine's own trail colours as swatches (its own first: unset, the game's own
// trail; then the others its burners have, read from the disc), the player's colour once set, the wheel, the
// brightness bar and the hex box. The colour is the racer's, whichever machine they race (TrailColour: the race's
// three words alike); the swatches are the machine's on show.
public sealed partial class GarageViewModel
{
    /// <summary>What the Colour tab colours on a custom machine: its three parts, and the trail.</summary>
    public static IReadOnlyList<string> CustomColourTabs { get; } = ["Body", "Cockpit", "Booster", "Trail"];

    /// <summary>What the Colour tab colours on a stock machine: the machine, and the trail.</summary>
    public static IReadOnlyList<string> StockColourTabs { get; } = ["Machine", "Trail"];

    /// <summary>The trail's picker.</summary>
    public TrailPicker Trail => _trail ??= new TrailPicker(TrailColour.Of(_state.Settings.Racer.Trail), SetTrail);

    private TrailPicker? _trail;

    /// <summary>The Colour tab shows the trail rather than the machine's (or a part's) colour.</summary>
    [ObservableProperty]
    public partial bool TrailPicked { get; set; }

    /// <summary>The Colour tab's choices on the shelf shown.</summary>
    public IReadOnlyList<string> ColourTabs => CustomShown ? CustomColourTabs : StockColourTabs;

    /// <summary>The choice beside the wheel: on a custom machine 0-2 a part, 3 the trail; on a stock machine 0 the
    /// machine, 1 the trail. (A list that loses its items sets -1: kept as it was.)</summary>
    public int ColourTabIndex
    {
        get => TrailPicked ? ColourTabs.Count - 1 : CustomShown ? Builder.PartTab : 0;
        set
        {
            if (value < 0 || value >= ColourTabs.Count || value == ColourTabIndex)
            {
                return;
            }
            if (value == ColourTabs.Count - 1)
            {
                TrailPicked = true;
                return;
            }
            if (CustomShown)
            {
                Builder.PartTab = value;
            }
            TrailPicked = false;
            OnPropertyChanged();
        }
    }

    partial void OnTrailPickedChanged(bool value) => ShowSections();

    /// <summary>The trail's swatches for the machine on show: a custom machine's booster's, else the stock machine's.</summary>
    private void ShowTrailSwatches()
    {
        if (MachineIndex < 0 && !CustomShown)
        {
            return;
        }
        var read = CustomShown ? Theme.GameSkin.CustomTrailSwatchesAsync(Builder.Build.Booster) : Theme.GameSkin.TrailSwatchesAsync(MachineIndex);
        _ = Trail.ShowAsync(read);
    }

    // the colour kept a moment after the last change (the wheel changes it as it is dragged)
    private int _trailChanges;
    private bool _trailWaiting;

    private void SetTrail(Rgb? colour)
    {
        _trailWaiting = true;
        _ = SaveTrailLaterAsync(++_trailChanges);
    }

    private async Task SaveTrailLaterAsync(int change)
    {
        await Task.Delay(SaveDelayMs);
        if (change == _trailChanges)
        {
            SaveTrailNow();
        }
    }

    /// <summary>Keeps the trail's colour now, if a change is waiting.</summary>
    private void SaveTrailNow()
    {
        if (!_trailWaiting)
        {
            return;
        }
        _trailWaiting = false;
        var trail = TrailColour.Trail(Trail.Colour);
        if (_state.Settings.Racer.Trail != trail)
        {
            _state.Update(s => s with { Racer = s.Racer with { Trail = trail } });
            LookSaved?.Invoke();
        }
    }
}

/// <summary>The trail's one colour, picked as a machine's colour is: swatch 0 its own (unset: the game's own trail),
/// 1-3 the machine's other trail colours, -1 the player's own colour; the wheel and the box set the player's.</summary>
public sealed partial class TrailPicker : ObservableObject
{
    private const string Unknown = "#5A6478";

    /// <summary>The most of the machine's own colours shown after its own (as a machine's four stock colours).</summary>
    private const int MostStock = 3;

    private readonly Action<Rgb?> _changed;
    private IReadOnlyList<Rgb> _stock = [];
    private Rgb? _paint;
    private bool _showing;
    private int _shows;

    public TrailPicker(Rgb? colour, Action<Rgb?> changed)
    {
        _changed = changed;
        Colour = colour;
        _paint = colour;
        Show();
    }

    /// <summary>The colour chosen; null: the game's own trail.</summary>
    public Rgb? Colour { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<StockSwatch> Swatches { get; set; } = [];

    /// <summary>The colour on the wheel and in the box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorValid))]
    public partial string ColorHex { get; set; } = "#FFFFFF";

    public bool ColorValid => Rgb.Parse(ColorHex) is not null;

    /// <summary>The machine's own trail colours once read (null: not known, only its own and the player's).</summary>
    public async Task ShowAsync(Task<IReadOnlyList<Rgb>?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var show = ++_shows;
        _stock = [];
        Show();
        var colours = await read;
        if (show == _shows)
        {
            _stock = colours ?? [];
            Show();
        }
    }

    partial void OnColorHexChanged(string value)
    {
        if (_showing || Rgb.Parse(value) is not { } color)
        {
            return;
        }
        // a colour picked on the wheel or typed: the player's
        _paint = color;
        Set(color);
    }

    /// <summary>A swatch: 0 the game's own trail, 1-3 one of the machine's colours, -1 the player's.</summary>
    [RelayCommand]
    private void PickSwatch(int k)
    {
        if (k == 0)
        {
            Set(null);
        }
        else if (k > 0 && k < _stock.Count)
        {
            Set(_stock[k]);
        }
        else if (k < 0 && _paint is { } paint)
        {
            Set(paint);
        }
    }

    private void Set(Rgb? colour)
    {
        Colour = colour;
        Show();
        _changed(colour);
    }

    /// <summary>The swatches and the wheel for the colour as it stands.</summary>
    private void Show()
    {
        var stock = Enumerable.Range(1, Math.Min(MostStock, Math.Max(0, _stock.Count - 1))).ToList();
        var picked = Colour is null ? 0 : stock.FirstOrDefault(k => _stock[k] == Colour, -1);
        var swatches = new List<StockSwatch> { new(0, _stock.Count > 0 ? Rgb.Hex(_stock[0]) : Unknown, "Its own", picked == 0) };
        swatches.AddRange(stock.Select(k => new StockSwatch(k, Rgb.Hex(_stock[k]), $"{k + 1}", picked == k)));
        if (_paint is { } paint)
        {
            swatches.Add(new StockSwatch(-1, Rgb.Hex(paint), "Yours", picked == -1));
        }
        Swatches = swatches;
        if ((Colour ?? (_stock.Count > 0 ? _stock[0] : null)) is { } shown)
        {
            _showing = true;
            ColorHex = Rgb.Hex(shown);
            _showing = false;
        }
    }
}
