using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// The garage: each machine set up on its own (<see cref="MachineLook"/>), its colour (one of its four stock colours,
/// its default first, or any colour painted on it) and its setting. The machine selected here is the player's machine,
/// the one raced; the name is set in the sidebar. Every change is kept as it
/// is made (a moment after the last one, so dragging over the colour wheel writes once), so moving to another machine
/// or page loses nothing and there is no Save to forget. Beside the colour it shows the machine's stats and puts the
/// machines in order by one of them (GarageViewModel.Stats.cs), and marks favourites and takes the lobby's part
/// (GarageViewModel.Lobby.cs): in a lobby the page is the machine choice too, and the machine selected is the one
/// the player races.
/// </summary>
public sealed partial class GarageViewModel : ViewModelBase
{
    private const int SaveDelayMs = 400;
    private readonly AppState _state;
    private int _changes;     // counts changes, so a save waits for the last
    private bool _changed;    // a change not kept yet
    private bool _loading;
    private bool _settingStock;
    private readonly GarageCard[] _cards;   // by id

    /// <param name="stats">Where the machines' stats come from (a test gives its own); the player's disc otherwise.</param>
    /// <param name="packs">The stat packs the Stats choice offers (a test gives its own); the launcher's own otherwise.</param>
    public GarageViewModel(AppState state, Func<Task<MachineStatTable?>>? stats = null, IReadOnlyList<StatPack>? packs = null)
    {
        _state = state;
        _readStats = stats ?? Theme.GameSkin.MachineStatsAsync;
        _packs = packs ?? state.PracticePackList;
        _cards = [.. Enumerable.Range(0, Machines.Names.Count).Select(id => new GarageCard(id))];
        foreach (var card in _cards)
        {
            card.Show(state.Settings.Racer.LookOf(card.Id));
        }
        Sort = state.Settings.GarageSort;
        ShowFavourites();
        Builder = new CustomBuilderViewModel(state);
        Builder.Raced += () =>
        {
            ShowSelected();                // (a custom machine raced, or none any more: which cell is lit)
            Picked?.Invoke(MachineIndex);   // the lobby sends the machine choice, which carries it
        };
        Builder.SavedChanged += ShowChoices;
        Builder.SavedAndRaced += () =>
        {
            if (!_confirming)
            {
                Confirm();   // (Save in the builder: the build raced)
            }
        };                     // (the custom machines' cards, the counts)
        Builder.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "" or nameof(CustomBuilderViewModel.Build) or nameof(CustomBuilderViewModel.Editing))
            {
                OnPropertyChanged(nameof(ShownBuild));
                OnPropertyChanged(nameof(CanRemove));
            }
        };
        LoadLenses();
        Shelf = FirstShelf();
        foreach (var tab in ShelfTabs)
        {
            tab.On = tab.Shelf == Shelf;
        }
        ShowChoices();
        ShowSections();
        // the garage opens on the machine last raced
        MachineIndex = Math.Clamp(state.Settings.Racer.Machine, 0, Machines.Names.Count - 1);
        Turning = !state.Racing;
        state.Changed += () => OnUi(() =>
        {
            Turning = !_state.Racing;
            ReadStatsOfAnotherDisc();
        });
        ShowSort();
        _ = ReadStatsAsync();
    }

    /// <summary>The builder of custom machines, the Garage's Custom shelf.</summary>
    public CustomBuilderViewModel Builder { get; }

    /// <summary>The machine turns slowly on its turntable; it rests while the game runs: the
    /// graphics card is the game's then.</summary>
    [ObservableProperty]
    public partial bool Turning { get; set; }

    /// <summary>The course whose venue sky stands behind the stage (the course drawn in a lobby, else the one picked
    /// for Practice); -1: the plain sky.</summary>
    [ObservableProperty]
    public partial int VenueCourse { get; set; } = -1;

    /// <summary>Every machine for the chooser, in the order shown (<see cref="Sort"/>). A card's place says nothing
    /// about its machine: that is its <see cref="GarageCard.Id"/>.</summary>
    public ObservableCollection<GarageCard> Choices { get; } = [];

    /// <summary>The machine being set up (shown in 3D).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MachineName))]
    public partial int MachineIndex { get; set; } = -1;

    /// <summary>One of the machine's stock colours (0-3), or null: <see cref="ColorHex"/> painted on it.</summary>
    [ObservableProperty]
    public partial int? StockColor { get; set; }

    /// <summary>The colour on the wheel and in the text box: the paint, or the chosen stock colour's swatch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorValid))]
    public partial string ColorHex { get; set; } = "#E8432E";

    /// <summary>The machine's four stock colours (grey until read, or without the disc), its default first, and the
    /// player's own paint when it has one.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StockSwatch> Swatches { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingText), nameof(ShownSetting))]
    public partial int Setting { get; set; } = 100;

    public string MachineName => Machines.Names[Math.Clamp(MachineIndex, 0, Machines.Names.Count - 1)];

    public bool ColorValid => Rgb.Parse(ColorHex) is not null;

    public string SettingText => ShownSetting.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // the paint this machine had or was given, kept while a stock colour is chosen so it can be picked again
    private Rgb? _paint;

    partial void OnMachineIndexChanging(int value) => SaveNow();

    partial void OnMachineIndexChanged(int value)
    {
        ShowSelected();
        OnPropertyChanged(nameof(SelectedFavourite));
        ShowStats();
        var look = _state.Settings.Racer.LookOf(value);
        _loading = true;
        _paint = look.Color;
        Setting = Math.Clamp(look.Setting, 0, 100);
        StockColor = look.StockColor;
        if (look.Color is { } color)
        {
            ColorHex = Rgb.Hex(color);
        }
        _loading = false;
        Swatches = Placeholders();
        _ = RefreshSwatchesAsync(value);
    }

    partial void OnColorHexChanged(string value)
    {
        if (_settingStock || _loading || Rgb.Parse(value) is not { } color)
        {
            return;
        }
        // a colour picked on the wheel or typed: painted
        _paint = color;
        StockColor = null;
        Swatches = [.. Swatches.Where(s => s.Index >= 0).Select(s => s with { Selected = false }), PaintSwatch(true)];
        SaveSoon();
    }

    partial void OnStockColorChanged(int? value) => SaveSoon();

    partial void OnSettingChanged(int value) => SaveSoon();

    /// <summary>A stock machine made the one raced (USE THIS MACHINE: <see cref="Use"/>): the machine being set up,
    /// and the player's machine from then on: kept, shown in the left column, queued with at the next join, and told
    /// on to a lobby (<see cref="Picked"/>).</summary>
    [RelayCommand]
    private void PickMachine(int id)
    {
        if (!Offers(id))
        {
            return;   // (a machine the lobby does not offer, shown greyed: never picked)
        }
        MachineIndex = id;
        if (_state.Settings.Racer.Machine != id || _state.Settings.Racer.Custom is not null)
        {
            // a stock machine picked: it races, not a custom one
            _state.Update(s => s with { Racer = s.Racer.WithMachine(id) with { Custom = null } });
        }
        ShowSelected();
        Picked?.Invoke(id);
    }

    /// <summary>Exactly one machine shows as the one raced: the stock tile SELECT stands on (<see cref="ChosenIndex"/>),
    /// unless a custom machine is raced (<see cref="RacerProfile.Raced"/>): then its tile alone, and no stock tile.</summary>
    private void ShowSelected()
    {
        var lit = _state.Settings.Racer.Raced.IsCustom ? -1 : ChosenIndex;
        foreach (var card in _cards)
        {
            card.Selected = card.Id == lit;
        }
    }

    /// <summary>A swatch: stock colour 0-3, or -1 the player's paint.</summary>
    [RelayCommand]
    private void PickSwatch(int k)
    {
        if (k < 0)
        {
            if (_paint is { } paint)
            {
                ColorHex = Rgb.Hex(paint);
                StockColor = null;
            }
        }
        else
        {
            StockColor = k;
            if (Swatches.FirstOrDefault(s => s.Index == k) is { Known: true } swatch)
            {
                // the wheel shows the stock colour, without painting it
                _settingStock = true;
                ColorHex = swatch.Hex;
                _settingStock = false;
            }
        }
        Swatches = [.. Swatches.Select(s => s with { Selected = s.Index == (StockColor ?? -1) })];
    }

    private async Task RefreshSwatchesAsync(int machine)
    {
        var colors = await Theme.GameSkin.StockSwatchesAsync(machine);
        if (machine != MachineIndex || colors is null)
        {
            return;
        }
        Swatches = WithPaint(Enumerable.Range(0, 4).Select(k => new StockSwatch(k, Rgb.Hex(colors[k]),
            k == 0 ? "Its own" : $"{k + 1}", StockColor == k, true)));
        if (StockColor is { } stock)
        {
            _settingStock = true;
            ColorHex = Rgb.Hex(colors[stock]);
            _settingStock = false;
        }
    }

    private List<StockSwatch> Placeholders() =>
        WithPaint(Enumerable.Range(0, 4).Select(k => new StockSwatch(k, "#5A6478", k == 0 ? "Its own" : $"{k + 1}", StockColor == k, false)));

    /// <summary>The stock swatches, and the player's paint after them when there is one.</summary>
    private List<StockSwatch> WithPaint(IEnumerable<StockSwatch> stock)
    {
        var all = stock.ToList();
        if (_paint is not null)
        {
            all.Add(PaintSwatch(StockColor is null));
        }
        return all;
    }

    private StockSwatch PaintSwatch(bool selected) => new(-1, Rgb.Hex(_paint!), "Yours", selected, true);

    /// <summary>Keeps the machine's look a moment after the last change.</summary>
    private void SaveSoon()
    {
        if (_loading || MachineIndex < 0)
        {
            return;
        }
        _changed = true;
        _ = SaveLaterAsync(++_changes);
        LookChanged?.Invoke();
    }

    private async Task SaveLaterAsync(int change)
    {
        await Task.Delay(SaveDelayMs);
        if (change == _changes)
        {
            SaveNow();   // no change since
        }
    }

    /// <summary>Keeps the look now, if a change is waiting. Only this machine's look: the name and the machine last
    /// raced are not the garage's.</summary>
    public void SaveNow()
    {
        if (!_changed || MachineIndex < 0)
        {
            return;
        }
        _changed = false;
        var look = new MachineLook(MachineIndex, StockColor, StockColor is null ? Rgb.Parse(ColorHex) ?? _paint : _paint,
            Math.Clamp(Setting, 0, 100));
        if (look.StockColor is null && look.Color is null)
        {
            return;   // nothing painted yet
        }
        if (_state.Settings.Racer.LookOf(MachineIndex) != look)
        {
            _state.Update(s => s with { Racer = s.Racer.WithLook(look) });
            _cards[look.Machine].Show(look);
            LookSaved?.Invoke();
        }
    }
}

/// <summary>One of a machine's colours on the garage page: a stock colour (Index 0-3) or the player's paint (-1);
/// Known once read from the disc.</summary>
public sealed record StockSwatch(int Index, string Hex, string Label, bool Selected, bool Known = true);

/// <summary>A machine in the garage's chooser, in its look as kept, lit when it is the one being set up.</summary>
public sealed partial class GarageCard(int id) : ObservableObject
{
    public int Id { get; } = id;

    [ObservableProperty]
    public partial bool Selected { get; set; }

    [ObservableProperty]
    public partial int? StockColor { get; set; }

    /// <summary>The paint as "#RRGGBB" when the look is painted.</summary>
    [ObservableProperty]
    public partial string? PaintColor { get; set; }

    /// <summary>Marked with the heart.</summary>
    [ObservableProperty]
    public partial bool Favourite { get; set; }

    /// <summary>The lobby offers it (always, out of a lobby); shown greyed and never picked when not.</summary>
    [ObservableProperty]
    public partial bool Offered { get; set; } = true;

    public void Show(MachineLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        PaintColor = look.StockColor is null && look.Color is { } c ? Rgb.Hex(c) : null;
        StockColor = look.StockColor is null && look.Color is null ? 0 : look.StockColor;
    }
}
