using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// The rows a rules panel shows (Controls/RulesPanel: Practice's set-up, a custom lobby's rules): a label
/// on the left and its value or its control on the right. Each kind of row is a type here, and the panel picks the
/// look by the type. A row keeps its value inside its range whatever it is given. The host's rules come as rows
/// from their one list (<see cref="HostRuleRows"/>).
/// </summary>
public abstract partial class RuleRow(string label) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>A line under the row, across the panel ("": none).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; set; } = "";

    public bool HasNote => Note.Length > 0;

    /// <summary>A control too wide to sit beside its label: it goes on a line under it, across the row.</summary>
    public virtual bool Wide => false;

    public int ControlRow => Wide ? 1 : 0;

    public int ControlColumn => Wide ? 0 : 1;

    public int ControlSpan => Wide ? 2 : 1;
}

/// <summary>A group of rows; a group after the first has a line above it (<see cref="Later"/>); a heading over it
/// where the panel has many (a custom lobby's: Race, Machines).</summary>
public sealed record RuleGroup(IReadOnlyList<RuleRow> Rows)
{
    public bool Later { get; init; }

    public string? Heading { get; init; }

    public bool HasHeading => Heading is { Length: > 0 };

    /// <summary>A panel's groups, in order.</summary>
    public static IReadOnlyList<RuleGroup> Of(params IReadOnlyList<RuleRow>[] groups) =>
        [.. groups.Select((rows, i) => new RuleGroup(rows) { Later = i > 0 })];
}

/// <summary>A value shown, nothing to set ("Rewind 1'00\"").</summary>
public sealed class ValueRule(string label, string value) : RuleRow(label)
{
    public string Value { get; } = value;

    /// <summary>The value differs from Quick Play's: shown in the accent.</summary>
    public bool Marked { get; init; }
}

/// <summary>The machine, with the Garage button beside it.</summary>
public sealed partial class MachineRule(string label, string value, Action garage) : RuleRow(label)
{
    [ObservableProperty]
    public partial string Value { get; set; } = value;

    [RelayCommand]
    private void Garage() => garage();
}

/// <summary>One of a list, dropped down.</summary>
public sealed partial class ChoiceRule(string label, IReadOnlyList<string> options, int selected = 0) : RuleRow(label)
{
    public IReadOnlyList<string> Options { get; } = options;

    [ObservableProperty]
    public partial int Selected { get; set; } = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));

    public string Chosen => Selected >= 0 && Selected < Options.Count ? Options[Selected] : "";

    partial void OnSelectedChanged(int value)
    {
        if (value < 0 || value >= Options.Count)
        {
            Selected = Math.Clamp(value, 0, Math.Max(0, Options.Count - 1));
        }
        OnPropertyChanged(nameof(Chosen));
    }
}

/// <summary>
/// A custom lobby's stat pack, dropped down as a choice is: Stock, the server's packs by name, and the host's own
/// file by its pack's name once one is chosen (Choose file beside it; Controls/RulesPanel opens the system's file
/// picker and hands the path to <see cref="Load"/>). A file that is no pack keeps the choice as it was and says why
/// in the line under the row.
/// </summary>
public sealed partial class PackRule : RuleRow
{
    private readonly IReadOnlyList<string> _served;

    /// <param name="served">The choices before the host's file: Stock and the server's packs.</param>
    /// <param name="selected">The choice among <paramref name="served"/>, or past them the host's file.</param>
    /// <param name="file">The host's own pack file, offered last (null: none yet).</param>
    public PackRule(string label, IReadOnlyList<string> served, int selected, Launcher.Core.HostPackFile? file) : base(label)
    {
        (_served, File) = (served, file);
        Options = Offered();
        Selected = Options.Count == 0 ? -1 : Math.Clamp(selected, 0, Options.Count - 1);   // (-1: nothing to choose yet)
    }

    /// <summary>The host's own pack file (null: none chosen this session).</summary>
    public Launcher.Core.HostPackFile? File { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Options { get; private set; }

    [ObservableProperty]
    public partial int Selected { get; set; }

    /// <summary>The host's file is the choice.</summary>
    public bool FileChosen => File is not null && Selected == _served.Count;

    /// <summary>The choice's name ("" for none).</summary>
    public string Chosen => Selected >= 0 && Selected < Options.Count ? Options[Selected] : "";

    partial void OnSelectedChanged(int value)
    {
        if ((value < 0 || value >= Options.Count) && Options.Count > 0)
        {
            Selected = Math.Clamp(value, 0, Options.Count - 1);
        }
        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(FileChosen));
    }

    /// <summary>The file at <paramref name="path"/> as the choice; a file that is no pack is said under the row.</summary>
    public void Load(string path)
    {
        var (file, problem) = Launcher.Core.HostPackFile.Read(path);
        if (file is null)
        {
            Note = problem ?? "";
            return;
        }
        Note = "";
        File = file;
        Options = Offered();
        Selected = Options.Count - 1;
        OnPropertyChanged(nameof(FileChosen));
    }

    private IReadOnlyList<string> Offered() => File is { } file ? [.. _served, file.Name] : _served;
}

/// <summary>One mode a custom lobby's host may choose: its name and its line of what it is (the header's about).</summary>
public sealed record ModeChoice(string Name, string About)
{
    public bool HasAbout => About.Length > 0;
}

/// <summary>
/// A custom lobby's game mode: the server's official modes (<see cref="LobbyList.Modes"/>, in its order), the lobby's own
/// mode when it is a host's file the server holds, and the host's own file once one is chosen (From file…: the view opens
/// the system's file picker and hands the path to <see cref="Load"/>). The create face lists <see cref="Choices"/>; the
/// host's lobby drops them down. A file that does not compile keeps the choice as it was and says why, with its line,
/// under the row. A mode file is one choice: its settings are its parameters, on their defaults.
/// </summary>
public sealed partial class GameModeRule : RuleRow
{
    /// <summary>The choice when the server lists no modes (one from before them): Quick Play's rules, no mode.</summary>
    public const string NoMode = "Quick Play rules";

    /// <param name="official">The server's official modes, in its order.</param>
    /// <param name="current">The lobby's mode as the server named it, with its values (null: none, a new lobby).</param>
    public GameModeRule(string label, IReadOnlyList<LobbyMode> official, LobbyMode? current) : base(label)
    {
        ArgumentNullException.ThrowIfNull(official);
        (Official, Current) = (official, current);
        Served = current is null || official.Any(m => m.Hash == current.Hash) ? null : current;
        Options = Offered();
        var at = current is null ? -1 : official.ToList().FindIndex(m => m.Hash == current.Hash);
        Selected = at >= 0 ? at : Served is not null ? official.Count : 0;
    }

    /// <summary>The server's official modes.</summary>
    public IReadOnlyList<LobbyMode> Official { get; }

    /// <summary>The lobby's mode as the server named it (its values are the lobby's): an official one is chosen with them.</summary>
    public LobbyMode? Current { get; }

    /// <summary>The lobby's mode when it is a host's own file the server holds: sent again by its hash, to keep it.</summary>
    public LobbyMode? Served { get; }

    /// <summary>The host's own mode file (null: none chosen while this panel is open).</summary>
    public Launcher.Core.HostModeFile? File { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Options { get; private set; }

    /// <summary>The choices with their lines, for the create face's list.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ModeChoice> Choices { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chosen))]
    public partial int Selected { get; set; }

    /// <summary>The choice in words.</summary>
    public ModeChoice? Chosen => Selected >= 0 && Selected < Choices.Count ? Choices[Selected] : null;

    /// <summary>The official mode chosen, with the lobby's values when it is the lobby's mode; null: another choice.</summary>
    public LobbyMode? OfficialChosen => Selected >= 0 && Selected < Official.Count
        ? Official[Selected].Hash == Current?.Hash ? Current : Official[Selected] : null;

    /// <summary>The host's file is the choice.</summary>
    public bool FileChosen => File is not null && Selected == Options.Count - 1;

    /// <summary>The lobby's own file, as the server holds it, is the choice.</summary>
    public bool ServedChosen => Served is not null && Selected == Official.Count && !FileChosen;

    /// <summary>A mode is chosen (not <see cref="NoMode"/>).</summary>
    public bool Any => OfficialChosen is not null || ServedChosen || FileChosen;

    /// <summary>The mode chosen as the server names one: the official's or the lobby's own; null for a file or none.</summary>
    public LobbyMode? Named => OfficialChosen ?? (ServedChosen ? Served : null);

    partial void OnSelectedChanged(int value)
    {
        if (value < 0 || value >= Options.Count)
        {
            Selected = Math.Clamp(value, 0, Math.Max(0, Options.Count - 1));
        }
    }

    /// <summary>The file at <paramref name="path"/> as the choice; a file that does not compile is said under the row.</summary>
    public void Load(string path)
    {
        var (file, problem) = Launcher.Core.HostModeFile.Read(path);
        Take(file, problem);
    }

    /// <summary>A file read (or why it is none) as the choice.</summary>
    public void Take(Launcher.Core.HostModeFile? file, string? problem)
    {
        if (file is null)
        {
            Note = problem ?? "";
            return;
        }
        Note = "";
        File = file;
        Options = Offered();
        Selected = Options.Count - 1;
        OnPropertyChanged(nameof(Selected));   // (the same place, another file: the rows follow it)
    }

    private IReadOnlyList<string> Offered()
    {
        Choices = [.. Official.Count == 0 && Served is null ? [new ModeChoice(NoMode, "")] : (ModeChoice[])[],
            .. Official.Select(m => new ModeChoice(m.Name, m.About)),
            .. Served is { } served ? [new ModeChoice(served.Name, served.About)] : (ModeChoice[])[],
            .. File is { } file ? [new ModeChoice(file.Name, file.About)] : (ModeChoice[])[]];
        return [.. Choices.Select(c => c.Name)];
    }
}

/// <summary>One of a few, side by side.</summary>
public sealed partial class SegmentedRule(string label, IReadOnlyList<string> options, int selected = 0) : RuleRow(label)
{
    public IReadOnlyList<string> Options { get; } = options;

    [ObservableProperty]
    public partial int Selected { get; set; } = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));
}

/// <summary>A small whole number between - and +.</summary>
public sealed partial class StepperRule(string label, int value, int minimum, int maximum) : RuleRow(label)
{
    public int Minimum { get; } = minimum;

    public int Maximum { get; } = maximum;

    /// <summary>How much a press changes it; a typed value goes to the nearest step from the least.</summary>
    public int Step { get; init; } = 1;

    /// <summary>What the number counts ("ms", "s"), shown after it; "": a plain number.</summary>
    public string Unit { get; init; } = "";

    public bool HasUnit => Unit.Length > 0;

    [ObservableProperty]
    public partial int Value { get; set; } = Math.Clamp(value, minimum, maximum);

    partial void OnValueChanged(int value)
    {
        var kept = Math.Clamp(value, Minimum, Maximum);
        if (Step > 1)
        {
            var steps = (int)Math.Round((kept - (double)Minimum) / Step);
            kept = Minimum + (Math.Min(steps, (Maximum - Minimum) / Step) * Step);
        }
        if (kept != value)
        {
            Value = kept;
        }
    }
}

/// <summary>Laps: a number between - and + that can be typed too, and beside it Unlimited (no lap count).</summary>
public sealed partial class LapsRule(string label, int value, int maximum) : RuleRow(label)
{
    public int Maximum { get; } = maximum;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Laps))]
    public partial int Value { get; set; } = Math.Clamp(value, 1, maximum);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Laps))]
    [NotifyPropertyChangedFor(nameof(Counted))]
    public partial bool Unlimited { get; set; }

    /// <summary>The stepper is in use: a lap count.</summary>
    public bool Counted => !Unlimited;

    /// <summary>The laps asked for: the number, or 0 when unlimited.</summary>
    public int Laps => Unlimited ? 0 : Value;

    partial void OnValueChanged(int value)
    {
        if (value < 1 || value > Maximum)
        {
            Value = Math.Clamp(value, 1, Maximum);
        }
    }
}

/// <summary>A whole number between - and +, or beside it the value with a name that states no number ("By course"): a mode's
/// optional parameter (<see cref="ModeParam.Or"/>), as Laps' Unlimited sits beside its stepper.</summary>
public sealed partial class NamedNumberRule(string label, string named, bool isNamed, int value, int minimum, int maximum) : RuleRow(label)
{
    /// <summary>The named value's words.</summary>
    public string Named { get; } = named;

    /// <summary>What the number counts ("ms", "s"), shown after it; "": a plain number.</summary>
    public string Unit { get; init; } = "";

    /// <summary>The unit shows: there is one and the number is in use.</summary>
    public bool UnitShown => Unit.Length > 0 && Counted;

    public int Minimum { get; } = minimum;

    public int Maximum { get; } = maximum;

    /// <summary>How much a press changes it.</summary>
    public int Step { get; init; } = 1;

    [ObservableProperty]
    public partial int Value { get; set; } = Math.Clamp(value, minimum, maximum);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Counted), nameof(State), nameof(UnitShown))]
    public partial bool IsNamed { get; set; } = isNamed;

    /// <summary>The stepper is in use: a number. A named value "Off" makes this the row's switch (On: the number).</summary>
    public bool Counted
    {
        get => !IsNamed;
        set => IsNamed = !value;
    }

    /// <summary>The named value is "Off": the row is a switch and its number, as any on/off row.</summary>
    public bool IsOff => Named == "Off";

    /// <summary>The switch's state in words, when <see cref="IsOff"/>.</summary>
    public string State => Counted ? "On" : "Off";

    partial void OnValueChanged(int value)
    {
        var kept = Math.Clamp(value, Minimum, Maximum);
        if (Step > 1)
        {
            kept = Minimum + ((int)Math.Round((kept - (double)Minimum) / Step) * Step);
            kept = Math.Min(kept, Maximum);
        }
        if (kept != value)
        {
            Value = kept;
        }
    }
}

/// <summary>A whole number along a bar, with its figure.</summary>
public sealed partial class SliderRule(string label, int value, int minimum, int maximum) : RuleRow(label)
{
    public int Minimum { get; } = minimum;

    public int Maximum { get; } = maximum;

    [ObservableProperty]
    public partial double Value { get; set; } = Math.Clamp(value, minimum, maximum);

    /// <summary>The value as the figure beside the bar shows it, a whole number.</summary>
    public int Whole => (int)Math.Round(Value);

    partial void OnValueChanged(double value)
    {
        if (value < Minimum || value > Maximum)
        {
            Value = Math.Clamp(value, Minimum, Maximum);
        }
        OnPropertyChanged(nameof(Whole));
    }
}

/// <summary>On or off.</summary>
public sealed partial class SwitchRule(string label, bool on) : RuleRow(label)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    public partial bool On { get; set; } = on;

    public string State => On ? "On" : "Off";
}

/// <summary>A time in milliseconds between - and +, on its steps, that can be typed too, with a switch that turns it
/// off ("Mines respawn": 200 ms, or off).</summary>
public sealed partial class DelayRule(string label, int ms, int minimum, int maximum, int step, int fallback) : RuleRow(label)
{
    public int Minimum { get; } = minimum;

    public int Maximum { get; } = maximum;

    public int Step { get; } = Math.Max(1, step);

    /// <summary>The time when on (kept while off, for when it is on again).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Ms))]
    public partial int Value { get; set; } = OnSteps(ms > 0 ? ms : fallback, minimum, maximum, Math.Max(1, step));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Ms))]
    public partial bool On { get; set; } = ms > 0;

    /// <summary>The time asked for: the value, or 0 when off.</summary>
    public int Ms => On ? Value : 0;

    partial void OnValueChanged(int value)
    {
        var kept = OnSteps(value, Minimum, Maximum, Step);
        if (kept != value)
        {
            Value = kept;
        }
    }

    // the nearest step inside the range
    private static int OnSteps(int value, int minimum, int maximum, int step) =>
        Math.Min(maximum, minimum + ((Math.Clamp(value, minimum, maximum) - minimum + (step / 2)) / step * step));
}

/// <summary>A line of text, with a hint while it is empty.</summary>
public sealed partial class TextRule(string label, string hint, int most = 32) : RuleRow(label)
{
    public string Hint { get; } = hint;

    public int Most { get; } = most;

    /// <summary>The text is a password: shown masked.</summary>
    public bool Secret { get; init; }

    /// <summary>What the box shows in place of each character ('\0': the text itself).</summary>
    public char Mask => Secret ? '•' : '\0';

    [ObservableProperty]
    public partial string Text { get; set; } = "";
}

/// <summary>A range of whole numbers, its lowest and highest; the lowest never above the highest.</summary>
public sealed partial class RangeRule(string label, int low, int high, int minimum, int maximum) : RuleRow(label)
{
    public override bool Wide => true;

    public int Minimum { get; } = minimum;

    public int Maximum { get; } = maximum;

    [ObservableProperty]
    public partial int Low { get; set; } = Math.Clamp(Math.Min(low, high), minimum, maximum);

    [ObservableProperty]
    public partial int High { get; set; } = Math.Clamp(Math.Max(low, high), minimum, maximum);

    partial void OnLowChanged(int value)
    {
        var kept = Math.Clamp(value, Minimum, Maximum);
        if (kept != value)
        {
            Low = kept;
        }
        else if (High < kept)
        {
            High = kept;
        }
    }

    partial void OnHighChanged(int value)
    {
        var kept = Math.Clamp(value, Minimum, Maximum);
        if (kept != value)
        {
            High = kept;
        }
        else if (Low > kept)
        {
            Low = kept;
        }
    }
}

/// <summary>
/// How a custom lobby's course is chosen: Vote, Random or Fixed side by side; Fixed names its one course, chosen in
/// the course picker (one course, as Practice picks it). Choosing Fixed opens the picker; a course pressed there is
/// the lobby's and closes it. Under a mode's list of courses the picker offers those alone.
/// </summary>
public sealed partial class CourseRule : RuleRow
{
    /// <summary>The choices, in <see cref="Online.Protocol.CourseSelectMode"/>'s order.</summary>
    public static IReadOnlyList<string> Choices { get; } = ["Vote", "Random", "Fixed"];

    public const int Fixed = 2;

    public CourseRule(string label, int selected, int course, IReadOnlyCollection<int>? only = null) : base(label)
    {
        Selected = Math.Clamp(selected, 0, Choices.Count - 1);
        Picker = new CoursePickerModel(PickMode.One, [course], only);
        Picker.Changed += () =>
        {
            OnPropertyChanged(nameof(Course));
            OnPropertyChanged(nameof(CourseName));
            Picking = false;
        };
        Picking = false;   // (a lobby that has its course already opens on the rules)
    }

    public IReadOnlyList<string> Options { get; } = Choices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixed))]
    public partial int Selected { get; set; }

    public bool IsFixed => Selected == Fixed;

    /// <summary>The one-course picker that holds the fixed course.</summary>
    public CoursePickerModel Picker { get; }

    /// <summary>The fixed course, by its stage number.</summary>
    public int Course => Picker.Picked;

    public string CourseName => Launcher.Core.Courses.Name(Course);

    /// <summary>The picker shows in place of the rules.</summary>
    [ObservableProperty]
    public partial bool Picking { get; set; }

    partial void OnSelectedChanged(int value)
    {
        if (value < 0 || value >= Choices.Count)
        {
            Selected = Math.Clamp(value, 0, Choices.Count - 1);
            return;
        }
        Picking = value == Fixed;
    }

    /// <summary>The course's name pressed: the picker again.</summary>
    [RelayCommand]
    private void PickCourse() => Picking = true;

    /// <summary>Done in the picker: the course stays as it is.</summary>
    [RelayCommand]
    private void ClosePicker() => Picking = false;
}
