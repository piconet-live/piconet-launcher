using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// A custom lobby's rules as the redesigned interface's option page (a new lobby's, or the host's own while it
/// waits), in steps, a few rows a screen: the lobby (its name, the password, the racers), then the race (the mode, the
/// course, the chosen mode's own settings under its name), and on request "More rules" (the vote's and the machine
/// choice's times, the voting minimum, the connection). NEXT goes on from the lobby; the race's step creates the lobby
/// (or saves the rules), or goes on to More rules; B goes a step back. The help panel says what the row the focus is on
/// does. Everything the rows hold is
/// the classic launcher's editor's (<see cref="LobbyRulesEditor"/>): this only puts its rows in another order.
/// </summary>
public sealed partial class RulesPageViewModel : ViewModelBase
{
    private readonly Dictionary<RuleRow, OptionItem> _items = [];

    /// <param name="eyebrow">Over the title: "CUSTOM LOBBY".</param>
    /// <param name="title">The page's title: "New lobby", "Edit rules".</param>
    /// <param name="action">The main action's words: "CREATE LOBBY", "SAVE RULES".</param>
    public RulesPageViewModel(LobbyRulesEditor editor, string eyebrow, string title, string action, ICommand act, ICommand back)
    {
        ArgumentNullException.ThrowIfNull(editor);
        (Editor, Eyebrow, Title, Action, ActCommand, BackCommand) = (editor, eyebrow, title, action, act, back);
        editor.PropertyChanged += OnEditorChanged;
        act.CanExecuteChanged += (_, _) => MainCommand.NotifyCanExecuteChanged();
        Regroup();
        Focused = FirstOfStep();
    }

    /// <summary>The step's first row.</summary>
    private OptionItem? FirstOfStep() => Shown is [{ Items: [var first, ..] }, ..] ? first : null;

    /// <summary>The steps' names, for the trail over the rows: the lobby, the race, and More rules when there are any.</summary>
    public string StepNames => HasMore ? "Lobby,Race,More rules" : "Lobby,Race";

    /// <summary>The step shown: 0 the lobby, 1 the race, 2 More rules.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown), nameof(MainText), nameof(MoreShown), nameof(Last))]
    public partial int Step { get; private set; }

    /// <summary>The groups of the step shown.</summary>
    public IReadOnlyList<OptionGroup> Shown => Step switch
    {
        0 => Lobby,
        1 => Basic.Skip(1).ToList(),
        _ => More,
    };

    /// <summary>The step after which the lobby is made (or the rules saved): the race's, or More rules.</summary>
    public bool Last => Step > 0;

    /// <summary>The main button's words: NEXT on the lobby's step, else the page's action ("CREATE LOBBY").</summary>
    public string MainText => Last ? Action : "NEXT";

    /// <summary>"More rules" beside the action, on the race's step.</summary>
    public bool MoreShown => Step == 1 && HasMore;

    /// <summary>The lobby's step's rows: the name, the password, the racers.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<OptionGroup> Lobby { get; private set; } = [];

    partial void OnStepChanged(int value) => Focused = FirstOfStep();

    /// <summary>The main button (and Start): on from the lobby's step, else the page's action.</summary>
    [RelayCommand(CanExecute = nameof(CanMain))]
    private void Main()
    {
        if (!Last)
        {
            Step = 1;
        }
        else if (ActCommand.CanExecute(null))
        {
            ActCommand.Execute(null);
        }
    }

    private bool CanMain() => !Last || ActCommand.CanExecute(null);

    /// <summary>"More rules": the step of the rest.</summary>
    [RelayCommand]
    private void ShowMore()
    {
        if (HasMore)
        {
            Step = 2;
        }
    }

    /// <summary>The page's Back: a step back, from the lobby's step the page's own way back.</summary>
    [RelayCommand]
    private void BackStep()
    {
        if (!StepBack())
        {
            BackCommand.Execute(null);
        }
    }

    /// <summary>B: a step back. False: the lobby's step shows (B leaves the page).</summary>
    public bool StepBack()
    {
        if (Step == 0)
        {
            return false;
        }
        Step--;
        return true;
    }

    public LobbyRulesEditor Editor { get; }

    public string Eyebrow { get; }

    public string Title { get; }

    public string Action { get; }

    public ICommand ActCommand { get; }

    public ICommand BackCommand { get; }

    /// <summary>The basic rows in their groups: the lobby's, the race's, and the mode's own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown))]
    public partial IReadOnlyList<OptionGroup> Basic { get; set; } = [];

    /// <summary>The rest: the step "More rules".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore), nameof(StepNames), nameof(MoreShown), nameof(Shown))]
    public partial IReadOnlyList<OptionGroup> More { get; set; } = [];

    public bool HasMore => More.Count > 0;

    /// <summary>The row the focus is on: the help panel's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HelpShown))]
    public partial OptionItem? Focused { get; set; }

    public bool HelpShown => Focused is not null;

    /// <summary>A line under the main action: a refusal, or why the lobby was not made.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The fixed course's picker shows over the page.</summary>
    public bool Picking => Editor.Picking;

    public CoursePickerModel Picker => Editor.CourseRow.Picker;

    /// <summary>"Done" in the picker.</summary>
    [RelayCommand]
    private void ClosePicker() => Editor.CourseRow.ClosePickerCommand.Execute(null);

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LobbyRulesEditor.RuleGroups) or nameof(LobbyRulesEditor.CreateGroups)
            or nameof(LobbyRulesEditor.CourseRow) or nameof(LobbyRulesEditor.ParamRows))
        {
            Regroup();
        }
        if (e.PropertyName is nameof(LobbyRulesEditor.Picking) or nameof(LobbyRulesEditor.CourseRow))
        {
            OnPropertyChanged(nameof(Picking));
            OnPropertyChanged(nameof(Picker));
        }
    }

    private OptionItem Item(RuleRow row, string group)
    {
        if (!_items.TryGetValue(row, out var item) || item.Group != group)
        {
            item = new OptionItem(row, group);
            _items[row] = item;
        }
        return item;
    }

    /// <summary>The groups for the rows the editor has now (another mode brings its own, and takes the rules it owns).</summary>
    private void Regroup()
    {
        var editor = Editor;
        var lobbyRows = editor.RuleGroups.SelectMany(g => g.Rows).ToHashSet();
        var racers = editor.Rule(Online.Protocol.HostRules.Racers.Name);
        var course = editor.CourseRow;
        var lobby = new List<RuleRow> { editor.NameRow };
        if (editor.Editing)
        {
            lobby.Add(editor.LockedRow);
        }
        lobby.Add(editor.PasswordRow);
        if (lobbyRows.Contains(racers))
        {
            lobby.Add(racers);
        }
        var race = new List<RuleRow> { editor.ModeRow };
        if (lobbyRows.Contains(course))
        {
            race.Add(course);
        }
        const string LobbyGroup = "Lobby", RaceGroup = "Race";
        var modeName = editor.ModeRow.Chosen?.Name ?? "Mode";
        var modeRows = editor.ParamRows.Select(p => p.Row).Concat(editor.PackFileShown ? [editor.PackFileRow] : []).ToList();
        List<OptionGroup> first =
        [
            new OptionGroup("", [.. lobby.Select(r => Item(r, LobbyGroup))]),
            new OptionGroup("", [.. race.Select(r => Item(r, RaceGroup))]),
        ];
        if (modeRows.Count > 0)
        {
            first.Add(new OptionGroup(modeName, [.. modeRows.Select(r => Item(r, modeName))]));
        }
        Lobby = [first[0]];
        Basic = first;
        More = [.. editor.RuleGroups
            .Select(g => (Heading: g.Heading ?? "", Rows: g.Rows.Where(r => r != racers && r != course).ToList()))
            .Where(g => g.Rows.Count > 0)
            .Select(g => new OptionGroup(g.Heading, [.. g.Rows.Select(r => Item(r, g.Heading))]))];
        if (Step == 2 && !HasMore)
        {
            Step = 1;
        }
    }
}
