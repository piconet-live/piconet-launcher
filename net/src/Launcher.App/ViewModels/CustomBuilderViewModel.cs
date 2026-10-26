using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Theme;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// The Garage's builder: the custom machines the player saved, as cards with
/// their names only; the machine being built in 3D with its parts named on it and its pilot; a list of each kind of
/// part and of all 41 pilots; each part's colour (CustomBuilderViewModel.Colour.cs) or the machine's stats, summed
/// from its parts as the game sums them. The name is the game's, from the three parts: never typed. A saved machine
/// is the one raced (<see cref="RacerProfile.Custom"/>) until a stock machine is picked; a card pressed is raced and
/// opened. Part names and stats come from the player's disc (<see cref="GameSkin.CustomPartsAsync"/>).
/// </summary>
public sealed partial class CustomBuilderViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly ICustomColours _colours;
    private CustomParts? _parts;

    // unsaved changes, kept by the machine they were made to (its card's place; -1 a new machine), so that opening
    // another machine loses none: back on it, they are there again
    private readonly Dictionary<int, CustomBuild> _drafts = [];

    public CustomBuilderViewModel(AppState state, ICustomColours? colours = null)
    {
        _state = state;
        _colours = colours ?? new PartColourWords(part => _parts?.StockWords(part));   // the disc's, once read
        Columns =
        [
            Column("Body", PartKind.Body), Column("Cockpit", PartKind.Cockpit), Column("Booster", PartKind.Booster),
            new PartColumn("Pilot", [.. Enumerable.Range(0, Pilots.Names.Count).Select(p => new PartChoice(p, Pilots.Names[p], 3))]),
        ];
        var saved = state.Settings.Racer.Customs ?? [];
        var raced = saved.ToList().FindIndex(IsRaced);
        Build = raced >= 0 ? saved[raced] : saved.Count > 0 ? saved[0] : CustomBuild.First;
        Editing = raced >= 0 ? raced : saved.Count > 0 ? 0 : null;
        ShowSaved();
        ShowBuild();
        _ = ReadPartsAsync();
        state.Changed += () => OnUi(ShowSaved);
    }

    private static PartColumn Column(string label, PartKind kind) =>
        new(label, [.. Enumerable.Range(CustomPartTables.First(kind), CustomPartTables.PerKind)
            .Select(id => new PartChoice(id, $"{label} {(id % CustomPartTables.PerKind) + 1}", (int)kind))]);

    /// <summary>A machine was made the one raced (a card pressed, Save) or no longer is (removed): the lobby sends it.</summary>
    public event Action? Raced;

    /// <summary>Save kept the build and raced it (the Garage's confirm sound).</summary>
    public event Action? SavedAndRaced;

    /// <summary>The saved machines, in the order saved.</summary>
    public ObservableCollection<CustomCard> Saved { get; } = [];

    /// <summary>The four lists: bodies, cockpits, boosters, pilots.</summary>
    public IReadOnlyList<PartColumn> Columns { get; }

    /// <summary>The machine being built.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(BodyName), nameof(CockpitName), nameof(BoosterName), nameof(PilotName))]
    public partial CustomBuild Build { get; set; }

    /// <summary>Which saved machine is open (its place in <see cref="Saved"/>); null: a new one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Favourite), nameof(CanFavourite))]
    public partial int? Editing { get; set; }

    /// <summary>The machine's name as the game makes it; "" before the disc's tables are read.</summary>
    public string Name => _parts?.Tables.NameOf(Build.Body, Build.Cockpit, Build.Booster) ?? "";

    public string BodyName => PartName(Build.Body);

    public string CockpitName => PartName(Build.Cockpit);

    public string BoosterName => PartName(Build.Booster);

    public string PilotName => Pilots.NameOf(Build.Pilot);

    /// <summary>The machine's stats, summed from its parts: label and value.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StatFigure> Stats { get; set; } = [];

    /// <summary>The build differs from the saved machine it was opened from (or is new).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool Unsaved { get; set; }

    /// <summary>"3": the saved machines' count, for the Custom tab.</summary>
    [ObservableProperty]
    public partial string CountText { get; set; } = "0";

    private string PartName(int id) => _parts?.Tables.PartName(id) is { Length: > 0 } name ? name
        : Columns[(int)CustomPartTables.KindOf(id)!].Choices[id % CustomPartTables.PerKind].Name;

    private async Task ReadPartsAsync()
    {
        _parts = await GameSkin.CustomPartsAsync();
        if (_parts is null)
        {
            return;
        }
        foreach (var choice in Columns.Take(3).SelectMany(c => c.Choices))
        {
            choice.Name = _parts.Tables.PartName(choice.Id);
        }
        ShowSaved();
        ShowBuild();
        OnPropertyChanged(string.Empty);   // the names, now the disc's
    }

    partial void OnBuildChanged(CustomBuild value) => ShowBuild();

    /// <summary>The build as it stands kept as a draft of the machine open, when it is not that machine as saved.</summary>
    private void KeepDraft()
    {
        var key = Editing ?? -1;
        if (Unsaved)
        {
            _drafts[key] = Build;
        }
        else
        {
            _drafts.Remove(key);
        }
    }

    /// <summary>The lists' marks, the stats, the colour panel and whether there is something to save.</summary>
    private void ShowBuild()
    {
        int[] picked = [Build.Body, Build.Cockpit, Build.Booster, Build.Pilot];
        for (var c = 0; c < Columns.Count; c++)
        {
            foreach (var choice in Columns[c].Choices)
            {
                choice.Selected = choice.Id == picked[c];
            }
        }
        ShowStats();
        var saved = _state.Settings.Racer.Customs ?? [];
        Unsaved = Editing is not { } i || i >= saved.Count || !saved[i].Equals(Build);
        ShowColour();
    }

    /// <summary>The cards as the settings hold them, the raced one lit.</summary>
    private void ShowSaved()
    {
        var saved = _state.Settings.Racer.Customs ?? [];
        while (Saved.Count > saved.Count)
        {
            Saved.RemoveAt(Saved.Count - 1);
        }
        for (var i = 0; i < saved.Count; i++)
        {
            var name = _parts?.Tables.NameOf(saved[i].Body, saved[i].Cockpit, saved[i].Booster) is { Length: > 0 } n ? n : $"Machine {i + 1}";
            if (i == Saved.Count)
            {
                Saved.Add(new CustomCard(i));
            }
            Saved[i].Name = name;
            Saved[i].Raced = IsRaced(saved[i]);
            Saved[i].Open = Editing == i;
            Saved[i].Favourite = saved[i].Favourite;
            // its still: the parts, each in its colour as the 3D view shows it
            var build = saved[i];
            if (!Equals(Saved[i].Build, build with { Favourite = false }))
            {
                Saved[i].Build = build with { Favourite = false };
            }
            List<Rgb?> colours = [.. Enumerable.Range(0, 3).Select(k => Shown(build.PartOf(k), build.ColourOf(k)))];
            if (!Saved[i].Colours.SequenceEqual(colours))
            {
                Saved[i].Colours = colours;
            }
        }
        CountText = saved.Count.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(Favourite));
        SavedChanged?.Invoke();
    }

    private bool IsRaced(CustomBuild build) => _state.Settings.Racer.Custom is { } raced
        && (raced.Body, raced.Cockpit, raced.Booster, raced.Pilot) == (build.Body, build.Cockpit, build.Booster, build.Pilot);

    /// <summary>A part or a pilot pressed in its list.</summary>
    [RelayCommand]
    private void PickPart(PartChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        Build = choice.Column switch
        {
            0 => Build with { Body = choice.Id },
            1 => Build with { Cockpit = choice.Id },
            2 => Build with { Booster = choice.Id },
            _ => Build with { Pilot = choice.Id },
        };
    }

    /// <summary>A card pressed: that machine opened, and raced.</summary>
    [RelayCommand]
    private void PickSaved(CustomCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var saved = _state.Settings.Racer.Customs ?? [];
        if (card.Index >= saved.Count)
        {
            return;
        }
        KeepDraft();
        Editing = card.Index;
        Build = _drafts.GetValueOrDefault(card.Index) ?? saved[card.Index];
        Race(saved[card.Index]);
    }

    /// <summary>A card opened (the Garage's PREVIEW): that machine in the builder, its draft if it was changed and not
    /// saved; nothing is raced (<see cref="Use"/> races it).</summary>
    [RelayCommand]
    private void OpenSaved(CustomCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var saved = _state.Settings.Racer.Customs ?? [];
        if (card.Index >= saved.Count)
        {
            return;
        }
        KeepDraft();
        Editing = card.Index;
        Build = _drafts.GetValueOrDefault(card.Index) ?? saved[card.Index];
        ShowSaved();
    }

    /// <summary>The machine open made the one raced (the Garage's USE THIS MACHINE): saved first when it was changed
    /// or is new (Save races it), else the saved machine raced as it is.</summary>
    [RelayCommand]
    private void Use()
    {
        var saved = _state.Settings.Racer.Customs ?? [];
        if (Unsaved)
        {
            Save();
        }
        else if (Editing is { } i && i < saved.Count)
        {
            Race(saved[i]);
        }
    }

    /// <summary>A new machine from the first parts; nothing is kept until it is saved.</summary>
    [RelayCommand]
    private void NewMachine()
    {
        KeepDraft();
        Editing = null;
        Build = _drafts.GetValueOrDefault(-1) ?? CustomBuild.First with { Pilot = Build.Pilot };
        ShowSaved();
    }

    /// <summary>Keeps the machine (over the one it was opened from, or as a new card) and races it.</summary>
    [RelayCommand(CanExecute = nameof(Unsaved))]
    private void Save()
    {
        var saved = (_state.Settings.Racer.Customs ?? []).ToList();
        _drafts.Remove(Editing is { } open && open < saved.Count ? open : -1);
        if (Editing is { } i && i < saved.Count)
        {
            saved[i] = Build with { Favourite = saved[i].Favourite };   // (the heart is the card's, not the build's)
        }
        else if (saved.Count < RacerProfile.MostCustoms)
        {
            saved.Add(Build with { Favourite = false });
            Editing = saved.Count - 1;
        }
        else
        {
            saved[^1] = Build with { Favourite = false };   // every place taken: the last one gives way
            Editing = saved.Count - 1;
        }
        _state.Update(s => s with { Racer = s.Racer with { Customs = saved } });
        Race(Build);
        SavedAndRaced?.Invoke();
    }

    /// <summary>The open machine removed; raced, the stock machine races again.</summary>
    [RelayCommand]
    private void Remove()
    {
        var saved = (_state.Settings.Racer.Customs ?? []).ToList();
        if (Editing is not { } i || i >= saved.Count)
        {
            NewMachine();
            return;
        }
        var wasRaced = IsRaced(saved[i]);
        saved.RemoveAt(i);
        _drafts.Clear();   // (the places move up)
        _state.Update(s => s with { Racer = s.Racer with { Customs = saved, Custom = wasRaced ? null : s.Racer.Custom } });
        Editing = saved.Count > 0 ? Math.Min(i, saved.Count - 1) : null;
        Build = Editing is { } next ? saved[next] : CustomBuild.First;
        ShowSaved();
        if (wasRaced)
        {
            Raced?.Invoke();
        }
    }

    private void Race(CustomBuild build)
    {
        var raced = build.AsRaced(_colours);
        _state.Update(s => s with { Racer = s.Racer with { Custom = raced } });
        ShowSaved();
        ShowBuild();
        Raced?.Invoke();
    }
}

/// <summary>A saved machine's tile: its name, its build and colours for its still; <see cref="Raced"/> lit,
/// <see cref="Open"/> the one being built.</summary>
public sealed partial class CustomCard(int index) : ObservableObject
{
    public int Index { get; } = index;

    /// <summary>Its parts (the still draws them; the heart left out, so a heart pressed draws nothing again).</summary>
    [ObservableProperty]
    public partial CustomBuild? Build { get; set; }

    /// <summary>Its parts' colours as the 3D view shows them: body, cockpit, booster (null a part's own).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Rgb?> Colours { get; set; } = [null, null, null];

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial bool Raced { get; set; }

    [ObservableProperty]
    public partial bool Open { get; set; }

    /// <summary>Marked with the heart: on the Garage's Favourites shelf too.</summary>
    [ObservableProperty]
    public partial bool Favourite { get; set; }
}

/// <summary>A part (or a pilot) in its list: its id, its name, which list (0 body, 1 cockpit, 2 booster, 3 pilot).</summary>
public sealed partial class PartChoice(int id, string name, int column) : ObservableObject
{
    public int Id { get; } = id;

    public int Column { get; } = column;

    [ObservableProperty]
    public partial string Name { get; set; } = name;

    [ObservableProperty]
    public partial bool Selected { get; set; }
}

/// <summary>One of the builder's lists, with its heading.</summary>
public sealed class PartColumn(string label, IReadOnlyList<PartChoice> choices)
{
    public string Label { get; } = label;

    public IReadOnlyList<PartChoice> Choices { get; } = choices;

    public string Count => Choices.Count.ToString(CultureInfo.InvariantCulture);
}
