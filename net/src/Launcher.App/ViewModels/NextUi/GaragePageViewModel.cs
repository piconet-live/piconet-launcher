using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// The redesigned Garage (Views/NextUi/GaragePageView), the Garage area's page and a lobby's whole Garage, in two
/// steps as F-Zero GX's machine select: first the machine (the shelves as chips with the search, the stats' lens and
/// Sort &amp; filter; the shelf's machines as a grid, New machine last on Custom; the one under the focus on the stage
/// with its grades), then, on a page of its own once a machine is pressed (<see cref="Detail"/>), what is done with it:
/// its grades and every value, its setting, its colours, "Use this machine" and the star (a custom machine's: Edit
/// parts, Remove). B goes back to the machines. The builder is an option page of its own
/// (<see cref="BuilderPageViewModel"/>). Everything it shows and keeps is the classic Garage's
/// (<see cref="GarageViewModel"/>); the stage is GarageViewModel.Stage.cs.
/// </summary>
public sealed partial class GaragePageViewModel : ViewModelBase
{
    /// <summary>How far one step of the setting moves it (the classic slider's tick).</summary>
    public const int SettingStep = 5;

    /// <summary>The setting's track on the card, in pixels: the marker stands along it.</summary>
    public const double SettingTrack = 300;

    private readonly ShellViewModel _shell;

    public GaragePageViewModel(GarageViewModel garage, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(garage);
        ArgumentNullException.ThrowIfNull(shell);
        Garage = garage;
        _shell = shell;
        garage.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(GarageViewModel.Swatches) or nameof(GarageViewModel.StagedCustom) or nameof(GarageViewModel.MachineIndex):
                    ShowSwatches();
                    ShowCard();
                    break;
                case nameof(GarageViewModel.StagedSummary):
                    ShowCard();
                    break;
                case nameof(GarageViewModel.Setting) or nameof(GarageViewModel.SettingRange) or nameof(GarageViewModel.ShownSetting):
                    ShowSetting();
                    break;
                case nameof(GarageViewModel.EmptyText):
                    OnPropertyChanged(nameof(EmptyText));
                    OnPropertyChanged(nameof(Empty));
                    break;
                case nameof(GarageViewModel.StagedInUse) or nameof(GarageViewModel.StagedUsable):
                    OnPropertyChanged(nameof(UseText));
                    OnPropertyChanged(nameof(CanUse));
                    UseCommand.NotifyCanExecuteChanged();
                    break;
                default:
                    break;
            }
        };
        garage.Builder.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "" or nameof(CustomBuilderViewModel.Build) or nameof(CustomBuilderViewModel.Swatches))
            {
                ShowSwatches();
            }
        };
        ShowSwatches();
        ShowCard();
        ShowSetting();
    }

    public GarageViewModel Garage { get; }

    public CustomBuilderViewModel Builder => Garage.Builder;

    /// <summary>The builder's option page shows in the Garage's place.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Browsing))]
    public partial bool Building { get; private set; }

    /// <summary>The Garage itself shows (not the builder).</summary>
    public bool Browsing => !Building;

    /// <summary>The second step: the machine on the stage on a page of its own, with what is done with it (its setting,
    /// its colours, Use this machine); false: the machines to choose from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Choosing))]
    public partial bool Detail { get; private set; }

    /// <summary>The first step shows: the machines to choose from.</summary>
    public bool Choosing => !Building && !Detail;

    partial void OnDetailChanged(bool value) => _shell.Stepped();

    partial void OnBuildingChanged(bool value) => OnPropertyChanged(nameof(Choosing));

    /// <summary>The builder shows its first step, the parts (Start: on to the colours).</summary>
    public bool BuilderOnParts => BuilderPage is { Step: 0 };

    /// <summary>The builder's page while it shows; null otherwise.</summary>
    [ObservableProperty]
    public partial BuilderPageViewModel? BuilderPage { get; private set; }

    /// <summary>The grades of the machine on the stage, as the game's machine select gives them: body, boost, grip.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StatFigure> Grades { get; private set; } = [];

    /// <summary>Its other figures on the card: the weight, and the stat the machines are sorted by.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StatFigure> Figures { get; private set; } = [];

    /// <summary>The card's colours: a stock machine's four and its paint, a custom machine's body's.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StockSwatch> Swatches { get; private set; } = [];

    /// <summary>The setting as a number (0 acceleration, 100 top speed).</summary>
    [ObservableProperty]
    public partial string SettingText { get; private set; } = "";

    /// <summary>Where the setting's marker stands along the track.</summary>
    [ObservableProperty]
    public partial double SettingOffset { get; private set; }

    /// <summary>The lobby's limit beside it: "Fixed", "80-100"; "" none.</summary>
    [ObservableProperty]
    public partial string SettingNote { get; private set; } = "";

    /// <summary>The main button's words: what pressing it does, or that the machine is the one raced.</summary>
    public string UseText => Garage.StagedInUse ? "IN USE" : "USE THIS MACHINE";

    public bool CanUse => !Garage.StagedInUse && Garage.StagedUsable;

    /// <summary>The shelf shows no machine: what to do about it.</summary>
    public bool Empty => Garage.EmptyText.Length > 0;

    public string EmptyText => Garage.EmptyText switch
    {
        "No favourites" => "No favourites yet. Press the star on a machine.",
        "No custom machines" => "No machines of your own yet. Build one with New machine.",
        "No match" => "No machine fits. Clear the search or the filter.",
        var words => words,
    };

    /// <summary>The page shows: the machine raced on the stage (the builder, when it was open, as it was).</summary>
    public void Shown()
    {
        if (!Building)
        {
            Garage.Stage(Garage.Current() ?? First());
        }
    }

    /// <summary>The page left: SELECT as it stood, the machine raced on the stage; the next visit begins with the
    /// machines again.</summary>
    public void Left()
    {
        if (!Building)
        {
            Detail = false;
            Garage.Unstage();
        }
    }

    /// <summary>B over a panel of the Garage's: a step back inside it (the colours: from the mix to the palette). False:
    /// the panel is on its first step (or is not the Garage's), and B closes it.</summary>
    public static bool StepBack(ViewModelBase? sheet) => sheet is IGarageSheet garage && garage.StepBack();

    /// <summary>B inside the page: the builder a step back, else the machine's page back to the machines. False: the
    /// page is on its first step (the builder's first: Back closes it).</summary>
    public bool StepBack()
    {
        if (Building)
        {
            return BuilderPage?.StepBack() == true;
        }
        if (Detail)
        {
            Detail = false;
            return true;
        }
        return false;
    }

    /// <summary>A tile of the strip has the focus: its machine on the stage.</summary>
    public void Focus(object? tile)
    {
        if (!Building && tile is GarageCard or CustomCard)
        {
            Garage.Stage(tile);
        }
    }

    /// <summary>A tile pressed: its machine on the stage and on its own page, the next step (only "Use this machine"
    /// makes it the one raced); New machine opens the builder.</summary>
    [RelayCommand]
    private void Pick(object? tile)
    {
        if (tile is NewMachineTile)
        {
            OpenBuilder(fresh: true);
            return;
        }
        if (!Building && tile is GarageCard or CustomCard)
        {
            Garage.Stage(tile);
            Detail = true;
        }
    }

    /// <summary>"Use this machine": the one raced, and the machines again with it marked.</summary>
    [RelayCommand(CanExecute = nameof(CanUse))]
    private void Use()
    {
        Garage.UseStaged();
        Detail = false;
    }

    /// <summary>The star.</summary>
    [RelayCommand]
    private void ToggleFavourite() => Garage.ToggleStagedFavourite();

    /// <summary>A shelf's chip: its machines, the one on the stage kept when the shelf holds it.</summary>
    [RelayCommand]
    private void ShowShelf(ShelfTab? tab)
    {
        if (tab is null)
        {
            return;
        }
        Garage.ShowShelfCommand.Execute(tab.Shelf);
        if (Garage.StagedTile() is null && (Garage.Current() ?? First()) is { } tile)
        {
            Garage.Stage(tile);
        }
    }

    /// <summary>"Stats: …": the next choice to see the stats through.</summary>
    [RelayCommand]
    private void NextLens() => Garage.NextLens();

    /// <summary>"Sort &amp; filter" (and Y): its panel.</summary>
    [RelayCommand]
    public void OpenSort() => _shell.Sheet = new SortSheetViewModel(Garage);

    /// <summary>"+" beside the colours: every colour, the machine's (a custom machine's body's) first.</summary>
    [RelayCommand]
    private void OpenColours() => OpenColoursOf(0);

    /// <summary>The colours panel on one of its targets: a stock machine's 0 the machine, 1 the trail; a custom
    /// machine's 0-2 a part, 3 the trail.</summary>
    public void OpenColoursOf(int target) => _shell.Sheet = new ColourSheetViewModel(Garage, target);

    /// <summary>"All stats": every value of the machine on the stage.</summary>
    [RelayCommand]
    private void OpenStats() => _shell.Sheet = new StatsSheetViewModel(Garage);

    /// <summary>A swatch on the card.</summary>
    [RelayCommand]
    private void PickSwatch(StockSwatch? swatch)
    {
        if (swatch is null)
        {
            return;
        }
        if (Garage.StagedCustom)
        {
            Builder.PickSwatchOf(0, swatch.Index);
        }
        else
        {
            Garage.PickSwatchCommand.Execute(swatch.Index);
        }
    }

    /// <summary>Left (-1) or right (1) on the setting: a step of it, inside the lobby's range.</summary>
    [RelayCommand(CanExecute = nameof(CanStepSetting))]
    private void StepSetting(int by) => Garage.ShownSetting = Stepped(by);

    private bool CanStepSetting(int by) => Garage.SettingFree && Stepped(by) != Garage.ShownSetting;

    private int Stepped(int by)
    {
        var range = Garage.SettingRange;
        var to = (int)Math.Round((Garage.ShownSetting + (Math.Sign(by) * SettingStep)) / (double)SettingStep) * SettingStep;
        return range.Clamp(Math.Clamp(to, 0, 100));
    }

    /// <summary>"Edit parts": the builder on the custom machine on the stage.</summary>
    [RelayCommand]
    private void EditParts()
    {
        if (Garage.StagedSaved)
        {
            OpenBuilder(fresh: false);
        }
    }

    /// <summary>"Remove": asked first.</summary>
    [RelayCommand]
    private void AskRemove()
    {
        if (Garage.StagedSaved)
        {
            _shell.AskRemoveMachine(Garage.StagedName);
        }
    }

    /// <summary>The custom machine on the stage removed (after the question); the stage shows the next one, or the
    /// machine raced.</summary>
    public void Remove()
    {
        if (!Garage.StagedSaved)
        {
            return;
        }
        Builder.RemoveCommand.Execute(null);
        Detail = false;
        if ((Garage.Current() ?? First()) is { } tile)
        {
            Garage.Stage(tile);
        }
        else
        {
            Garage.Unstage();
        }
    }

    /// <summary>The builder's page, on a new machine or on the custom machine on the stage.</summary>
    public void OpenBuilder(bool fresh)
    {
        if (Garage.CustomsOff)
        {
            return;
        }
        if (fresh)
        {
            Garage.StageFresh();
        }
        BuilderPage = new BuilderPageViewModel(this);
        BuilderPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuilderPageViewModel.Step))
            {
                _shell.Stepped();   // (NEXT, then Save: hints of their own)
            }
        };
        Building = true;
    }

    /// <summary>The builder's Back: the Garage again; a change not saved is kept for when the machine is opened again
    /// (the classic builder's drafts).</summary>
    public void CloseBuilder()
    {
        if (!Building)
        {
            return;
        }
        BuilderPage?.Dispose();
        BuilderPage = null;
        Building = false;
        if ((Garage.StagedTile() ?? Garage.Current() ?? First()) is { } tile)
        {
            Garage.Stage(tile);
        }
        else
        {
            Garage.Unstage();
        }
    }

    /// <summary>"Save machine": kept and raced (as the classic Save), and the Garage again with it on the stage, on a
    /// shelf that holds it.</summary>
    public void SaveBuild()
    {
        if (Builder.Unsaved || !Garage.StagedInUse)
        {
            Garage.UseStaged();
        }
        if (Garage.Shelf == GarageViewModel.StockShelf || (Garage.Shelf == GarageViewModel.FavouritesShelf && !Builder.Favourite))
        {
            Garage.ShowShelfCommand.Execute(GarageViewModel.CustomShelf);
        }
        CloseBuilder();
    }

    /// <summary>The shelf's first machine (not New machine).</summary>
    private object? First() => Garage.Roster.FirstOrDefault(t => t is not NewMachineTile);

    private void ShowSwatches()
    {
        IReadOnlyList<StockSwatch> swatches = Garage.StagedCustom ? Builder.SwatchesOf(0) : Garage.Swatches;
        if (!swatches.SequenceEqual(Swatches))
        {
            Swatches = swatches;
        }
    }

    /// <summary>The grades (a letter each) and the other figures of the machine on the stage.</summary>
    private void ShowCard()
    {
        var summary = Garage.StagedSummary;
        Grades = [.. summary.Where(IsGrade)];
        Figures = [.. summary.Where(f => !IsGrade(f)).Select(f => f.Label.Length == 0 ? f with { Label = "Weight" } : f)];
    }

    private static bool IsGrade(StatFigure figure) =>
        figure.Label.Length > 0 && figure.Value.Length == 1 && MachineFilter.Letters.Contains(figure.Value, StringComparison.Ordinal);

    private void ShowSetting()
    {
        var value = Garage.ShownSetting;
        SettingText = value.ToString(CultureInfo.InvariantCulture);
        SettingOffset = Math.Clamp(value, 0, 100) / 100.0 * SettingTrack;
        SettingNote = Garage.SettingLimited ? Garage.SettingLimit : "";
        StepSettingCommand.NotifyCanExecuteChanged();
    }
}
