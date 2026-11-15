using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// The redesigned Garage's builder as an option page ("New machine", "Edit machine") in two steps: first the parts
/// (body, cockpit, booster) and the pilot as rows that left and right go through, then each part's colour and the
/// trail's as colour rows (A: every colour, in the colours panel); the row the focus is on explained; beside them the
/// machine in 3D with its name, which the game makes from its parts, and its grades. NEXT goes on to the colours,
/// "Save machine" keeps it and races it, as the classic Save does; B goes a step back.
/// The rows work the classic builder (<see cref="CustomBuilderViewModel"/>), which keeps a change not saved as a draft of
/// its machine.
/// </summary>
public sealed partial class BuilderPageViewModel : ViewModelBase, IDisposable
{
    private static readonly string[] PartLabels = ["Body", "Cockpit", "Booster", "Pilot"];

    private readonly GaragePageViewModel _page;
    private readonly ChoiceRow[] _parts;
    private readonly SwatchRule[] _colours;
    private int _booster;

    public BuilderPageViewModel(GaragePageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
        var builder = page.Builder;
        _parts = [.. Enumerable.Range(0, 4).Select(k =>
        {
            IReadOnlyList<string> names = [.. builder.Columns[k].Choices.Select(c => c.Name)];
            return new ChoiceRow(PartLabels[k], () => names, () => PartIndex(k), i => builder.PickPartCommand.Execute(builder.Columns[k].Choices[i]));
        })];
        _colours =
        [
            .. Enumerable.Range(0, 3).Select(k => new SwatchRule($"{PartLabels[k]} colour", i => builder.PickSwatchOf(k, i), () => page.OpenColoursOf(k))),
            new SwatchRule("Trail colour", i => page.Garage.Trail.PickSwatchCommand.Execute(i), () => page.OpenColoursOf(3)),
        ];
        Groups =
        [
            new OptionGroup("Parts", [.. _parts.Select(r => new OptionItem(r, "Parts"))]),
            new OptionGroup("Colours", [.. _colours.Select(r => new OptionItem(r, "Colours"))]),
        ];
        Focused = Groups[0].Items[0];
        _booster = builder.Build.Booster;
        builder.PropertyChanged += OnBuilder;
        page.Garage.Trail.PropertyChanged += OnTrail;
        Show();
    }

    public GarageViewModel Garage => _page.Garage;

    public CustomBuilderViewModel Builder => _page.Builder;

    /// <summary>"New machine", or "Edit machine" for a saved one.</summary>
    public string Title => Builder.Editing is null ? "New machine" : "Edit machine";

    /// <summary>The parts, then the colours.</summary>
    public IReadOnlyList<OptionGroup> Groups { get; }

    /// <summary>The step shown: 0 the parts, 1 the colours.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown), nameof(MainText))]
    public partial int Step { get; private set; }

    /// <summary>The step's rows.</summary>
    public IReadOnlyList<OptionGroup> Shown => [Groups[Math.Clamp(Step, 0, Groups.Count - 1)]];

    /// <summary>The main button's words: NEXT on the parts, SAVE MACHINE on the colours.</summary>
    public string MainText => Step == 0 ? "NEXT" : "SAVE MACHINE";

    partial void OnStepChanged(int value) => Focused = Shown[0].Items is [var first, ..] ? first : null;

    /// <summary>The main button (and Start): on to the colours, then saved.</summary>
    [RelayCommand]
    private void Main()
    {
        if (Step == 0)
        {
            Step = 1;
        }
        else
        {
            Save();
        }
    }

    /// <summary>B: a step back. False: the parts show (B leaves the builder).</summary>
    public bool StepBack()
    {
        if (Step == 0)
        {
            return false;
        }
        Step = 0;
        return true;
    }

    /// <summary>The row the focus is on (its line under the machine).</summary>
    [ObservableProperty]
    public partial OptionItem? Focused { get; set; }

    /// <summary>The machine's name as the game makes it from its parts.</summary>
    public string Name => Builder.Name.Length > 0 ? Builder.Name : RacedMachine.UnnamedCustom;

    /// <summary>Its grades (body, boost, grip).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StatFigure> Grades { get; private set; } = [];

    /// <summary>What saving does beyond keeping it: a new machine with every place taken replaces the last.</summary>
    public string Notice => Builder.Editing is null && Builder.Saved.Count >= RacerProfile.MostCustoms
        ? $"You have {RacerProfile.MostCustoms} machines. Saving replaces the last one."
        : "";

    /// <summary>"Save machine": kept, raced, and the Garage again.</summary>
    [RelayCommand]
    private void Save() => _page.SaveBuild();

    /// <summary>Back: a step back, from the parts the Garage again (a change not saved is kept for this machine).</summary>
    [RelayCommand]
    private void Back()
    {
        if (!StepBack())
        {
            _page.CloseBuilder();
        }
    }

    public void Dispose()
    {
        Builder.PropertyChanged -= OnBuilder;
        Garage.Trail.PropertyChanged -= OnTrail;
    }

    private void OnBuilder(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(CustomBuilderViewModel.Build) or nameof(CustomBuilderViewModel.Editing)
            or nameof(CustomBuilderViewModel.Summary))
        {
            Show();
        }
    }

    private void OnTrail(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrailPicker.Swatches))
        {
            _colours[3].Show(Garage.Trail.Swatches);
        }
    }

    /// <summary>The rows, the name and the grades for the build as it stands.</summary>
    private void Show()
    {
        foreach (var part in _parts)
        {
            part.Show();
        }
        for (var k = 0; k < 3; k++)
        {
            _colours[k].Show(Builder.SwatchesOf(k));
        }
        _colours[3].Show(Garage.Trail.Swatches);
        if (Builder.Build.Booster != _booster)
        {
            _booster = Builder.Build.Booster;
            Garage.ShowStagedTrail();   // (another booster: its own trail colours)
        }
        Grades = [.. Builder.Summary.Where(f => f.Label.Length > 0 && f.Value.Length == 1)];
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Notice));
    }

    /// <summary>Where the build's part (0 body, 1 cockpit, 2 booster, 3 pilot) stands in its column.</summary>
    private int PartIndex(int column)
    {
        var build = Builder.Build;
        var id = column switch
        {
            0 => build.Body,
            1 => build.Cockpit,
            2 => build.Booster,
            _ => build.Pilot,
        };
        return Math.Max(0, Builder.Columns[column].Choices.ToList().FindIndex(c => c.Id == id));
    }
}
