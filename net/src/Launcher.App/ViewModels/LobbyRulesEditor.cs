using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// A custom lobby's settings as its host sets them, for a new lobby or theirs while it waits: pick a mode, then its
/// settings. The mode is one of the server's official modes (<see cref="LobbyList.Modes"/>: Quick Play's normal mode first,
/// then the others) or the host's own <c>.fzmode</c> file (From file…); its settings are its parameters on their
/// defaults, which the host changes (the CPUs, the teams, the stats among them, when the mode offers them). How the race
/// plays is the mode's (design D10): beside it the host sets only the lobby's own settings (the name, the password, the
/// racers, the ping limit, the vote's and the machine choice's times, the voting minimum, how the course is found among
/// the mode's), and the host's own stats file when the mode's stats choice is the file.
/// <see cref="Rules"/> holds only the lobby settings that differ from Quick Play's: a missing one is Quick Play's.
/// </summary>
public sealed partial class LobbyRulesEditor : ObservableObject
{

    private readonly LobbyRules? _quickPlay;
    private readonly LobbyRules _start;
    private readonly Dictionary<string, RuleRow> _rows = [];
    private IReadOnlyList<ModeParamRow> _params = [];

    /// <param name="start">The rules the panel opens on (a lobby's own; null: a new lobby, on Quick Play's).</param>
    /// <param name="quickPlay">The server's Quick Play rules (null: the rules' defaults).</param>
    /// <param name="modes">The server's official modes, in its order (<see cref="LobbyList.Modes"/>).</param>
    /// <param name="name">The lobby's name.</param>
    /// <param name="locked">The lobby has a password now (editing a lobby: the switch that keeps or clears it).</param>
    /// <param name="editing">The host's own lobby (not a new one): the password is kept unless changed.</param>
    /// <param name="file">The host's own stat pack file sent last (<see cref="QuickPlaySession.HostPack"/>): the pack
    /// file row's when the mode's stats choice is the file.</param>
    public LobbyRulesEditor(LobbyRules? start, LobbyRules? quickPlay, IReadOnlyList<LobbyMode> modes, string name, bool locked = false,
        bool editing = false, HostPackFile? file = null)
    {
        ArgumentNullException.ThrowIfNull(modes);
        (_quickPlay, Editing) = (quickPlay, editing);
        _start = start ?? new LobbyRules(new Dictionary<string, int>());
        NameRow = new TextRule("Name", "Lobby name", CustomLobby.MaxName) { Text = name };
        PasswordRow = editing
            ? new TextRule("New password", locked ? "Unchanged" : "None", CustomLobby.MaxPassword) { Secret = true }
            : new TextRule("Password", "None", CustomLobby.MaxPassword) { Secret = true };
        LockedRow = new SwitchRule("Password", locked);
        ModeRow = new GameModeRule("Mode", modes, _start.Mode);
        ModeRow.PropertyChanged += OnRowChanged;
        _params = NewParamRows();
        PackFileRow = new PackRule("Stats file", [], 0, file);
        PackFileRow.PropertyChanged += OnRowChanged;
        foreach (var rule in CustomRules.HostGroups.SelectMany(g => g.Rules))
        {
            var row = HostRuleRows.ForLobby(rule, _start, _quickPlay, null, null, ModeCourses);
            row.PropertyChanged += OnRowChanged;
            _rows[rule.Name] = row;
        }
        NewRacersRow();
        Regroup();
    }

    /// <summary>The host's own lobby: the password row changes it (empty: kept), the switch clears it.</summary>
    public bool Editing { get; }

    public TextRule NameRow { get; }

    public TextRule PasswordRow { get; }

    /// <summary>While editing: the lobby keeps a password (off: none).</summary>
    public SwitchRule LockedRow { get; }

    /// <summary>The lobby's game mode: an official one, the lobby's own file, or the host's file (From file…).</summary>
    public GameModeRule ModeRow { get; }

    /// <summary>The host's own stat pack file (Choose file…): shown when the mode's stats choice is the file.</summary>
    public PackRule PackFileRow { get; }

    /// <summary>The mode's stats choice is the host's own file now.</summary>
    public bool PackFileShown => _params.Any(p => p.FileChosen);

    /// <summary>The rows of the chosen mode's settings.</summary>
    public IReadOnlyList<ModeParamRow> ParamRows => _params;

    /// <summary>How the course is chosen, with the fixed course and its picker.</summary>
    public CourseRule CourseRow => (CourseRule)_rows[HostRules.CourseSelect.Name];

    /// <summary>The course picker shows in place of the rules.</summary>
    public bool Picking => CourseRow.Picking;

    /// <summary>The create face's panel: the name and the password, then the chosen mode's settings under its name.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RuleGroup> CreateGroups { get; private set; } = [];

    /// <summary>The host's lobby's first panel: the name and the password, then the mode (dropped down) and its settings.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RuleGroup> LobbyGroups { get; private set; } = [];

    /// <summary>The settings beside the mode, in their groups (with headings), but those the mode owns.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RuleGroup> RuleGroups { get; private set; } = [];

    /// <summary>The chosen mode's settings as the server takes them, in its package's order (null: none).</summary>
    private List<int>? ModeValues => _params.Count > 0 ? [.. _params.Select(p => p.Value)] : null;

    /// <summary>The chosen mode's declared settings and their values: a file's on their defaults, a named mode's as named.</summary>
    private (IReadOnlyList<ModeParam> Declared, IReadOnlyList<int>? Values) Declared() =>
        ModeRow.FileChosen ? (ModeRow.File!.Params, null)
        : ModeRow.Named is { Params: { } declared } named ? (declared, named.Values)
        : ([], null);

    private IReadOnlyList<ModeParamRow> NewParamRows()
    {
        foreach (var row in _params)
        {
            row.Row.PropertyChanged -= OnRowChanged;
        }
        var (declared, values) = Declared();
        var stats = ModeRow.FileChosen ? ModeRow.File!.Terms.PackParam : ModeRow.Named?.PackParam;
        IReadOnlyList<ModeParamRow> rows = [.. declared.Select((p, i) =>
            new ModeParamRow(p, values is { } v && v.Count == declared.Count ? v[i] : p.Default, i == stats))];
        foreach (var row in rows)
        {
            row.Row.PropertyChanged += OnRowChanged;
        }
        return rows;
    }

    /// <summary>The rules the chosen mode owns, by their keys: their rows do not show (the course's when it pins one).
    /// The player limit stays the lobby's, within the mode's <c>racers</c> (<see cref="NewRacersRow"/>).</summary>
    private HashSet<string> Owned() =>
        new(((ModeRow.FileChosen ? ModeRow.File!.Terms.Rules().Keys : ModeRow.Named?.Terms?.Keys) ?? []).Where(k => k != HostRules.Racers.Name), StringComparer.Ordinal);

    /// <summary>The chosen mode's racers (least, most); null: it states none.</summary>
    private (int Least, int Most)? ModeRacers => ModeRow.FileChosen ? ModeRow.File!.Terms.Racers
        : ModeRow.Named?.Terms is { } terms && terms.TryGetValue(HostRules.Racers.Name, out var most) ? (HostRules.Racers.Least, most) : null;

    /// <summary>The player limit's row again, within the chosen mode's racers: its most the mode's most.</summary>
    private void NewRacersRow()
    {
        var name = HostRules.Racers.Name;
        var old = (StepperRule)_rows[name];
        var (least, most) = ModeRacers is { } r ? (Math.Max(HostRules.Racers.Least, r.Least), Math.Min(HostRules.Racers.Most, r.Most)) : (HostRules.Racers.Least, HostRules.Racers.Most);
        if ((old.Minimum, old.Maximum) == (least, most))
        {
            return;
        }
        old.PropertyChanged -= OnRowChanged;
        var row = new StepperRule(old.Label, old.Value, least, most) { Step = old.Step, Unit = old.Unit };
        row.PropertyChanged += OnRowChanged;
        _rows[name] = row;
    }

    /// <summary>The courses the chosen mode lists; null: it lists none (or says <c>courses all</c>), and the row offers every course.</summary>
    private IReadOnlyList<int>? ModeCourses =>
        (ModeRow.FileChosen ? ModeRow.File!.Terms.Courses : ModeRow.Named?.Courses) is { Count: > 0 } list ? list : null;

    /// <summary>The course row again, for the mode chosen now: a fixed course outside its list moves to the list's first.</summary>
    private void NewCourseRow()
    {
        var name = HostRules.CourseSelect.Name;
        var old = CourseRow;
        old.PropertyChanged -= OnRowChanged;
        var row = new CourseRule(old.Label, old.Selected, old.Course, ModeCourses);
        row.PropertyChanged += OnRowChanged;
        _rows[name] = row;
        OnPropertyChanged(nameof(CourseRow));
        OnPropertyChanged(nameof(Picking));
    }

    /// <summary>The panels' groups for the mode chosen now.</summary>
    private void Regroup()
    {
        RuleRow[] first = Editing ? [NameRow, LockedRow, PasswordRow] : [NameRow, PasswordRow];
        RuleRow[] file = PackFileShown ? [PackFileRow] : [];
        RuleRow[] settings = [.. _params.Select(p => p.Row), .. file];
        // the create face: the mode's settings under its name; none: a line that says so
        CreateGroups = [new RuleGroup(first), new RuleGroup(settings.Length > 0 ? settings : [new ValueRule("Settings", "None")])
        {
            Later = true,
            Heading = ModeRow.Chosen?.Name ?? ModeRow.Label,
        }];
        LobbyGroups = RuleGroup.Of(first, [ModeRow, .. settings]);
        Grouped = PackFileShown;
        var owned = Owned();
        RuleGroups = [.. CustomRules.HostGroups.Select(g => (g.Heading, Rows: g.Rules.Where(r => !owned.Contains(r.Name)).Select(r => _rows[r.Name]).ToList()))
            .Where(g => g.Rows.Count > 0).Select((g, i) => new RuleGroup(g.Rows)
            {
                Later = i > 0,
                Heading = g.Heading,
            })];
    }

    /// <summary>The settings as the rows hold them now: the lobby settings that differ from Quick Play's, the host's own
    /// stats file when the mode's stats choice is the file, and the mode with its settings (an official or the lobby's by
    /// its hash, a new file whole).</summary>
    public LobbyRules Rules
    {
        get
        {
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            var owned = Owned();
            foreach (var rule in CustomRules.HostGroups.SelectMany(g => g.Rules).Where(r => !owned.Contains(r.Name)))
            {
                HostRuleRows.Write(rule, _rows[rule.Name], values);
            }
            var against = _quickPlay ?? CustomRules.Defaults;
            var differing = values.Where(p => against.Value(p.Key) != p.Value).ToDictionary(p => p.Key, p => p.Value);
            if (values.TryGetValue(LobbyRules.FixedCourseKey, out var course))
            {
                (differing[HostRules.CourseSelect.Name], differing[LobbyRules.FixedCourseKey]) = ((int)CourseSelectMode.Fixed, course);
            }
            // the host's own stats file, only when the mode's stats choice is the file
            var file = PackFileShown ? HostRuleRows.PackFile(PackFileRow) : null;
            return new LobbyRules(differing, file is null ? null : PackFileRow.File!.Name, file,
                ModeRow.Named is { } named ? named with { Values = ModeValues } : null, ModeRow.FileChosen ? ModeRow.File!.Text : null, ModeValues);
        }
    }

    /// <summary>The settings in one line, the mode first: its name, its settings moved from their defaults, then the
    /// lobby settings that differ from Quick Play's.</summary>
    public string Summary => Rules is var rules && ModeRow.FileChosen && ModeRow.File is { } file
        ? string.Join(" · ", new[] { file.Name, CustomRules.ParamsLine(file.Params, ModeValues, file.Terms.PackParam), CustomRules.CoursesLine(file.Terms.Courses), CustomRules.MachinesLine(file.Terms.Machines), CustomRules.CustomsLine(file.Terms.CustomsOff), CustomRules.Summary(rules with { Mode = null, ModeValues = null, StatPack = null }, _quickPlay) }.Where(s => s.Length > 0))
        // (the named mode's terms are on the values it came with, not the host's moved ones: its settings by their labels)
        : CustomRules.Summary(rules with { Mode = rules.Mode is { } named ? named with { Terms = null } : null }, _quickPlay);

    /// <summary>The name as a request sends it: trimmed.</summary>
    public string Name => NameRow.Text.Trim();

    /// <summary>The name can be sent.</summary>
    public bool NameValid => Name.Length is > 0 and <= CustomLobby.MaxName;

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CourseRule.Picking))
        {
            OnPropertyChanged(nameof(Picking));
            return;
        }
        if (sender == ModeRow && e.PropertyName is nameof(GameModeRule.Selected) or nameof(GameModeRule.Options))
        {
            // the mode's settings come, its courses; the rows it owns go
            _params = NewParamRows();
            NewCourseRow();
            NewRacersRow();
            Regroup();
            OnPropertyChanged(nameof(ParamRows));
        }
        else if (_params.Any(p => p.Row == sender) && PackFileShown != Grouped)
        {
            Regroup();   // (the stats choice moved to or from the file: its row comes or goes)
        }
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>The panels were grouped with the pack file's row.</summary>
    private bool Grouped { get; set; }

    /// <summary>A row by its rule's name.</summary>
    public RuleRow Rule(string name) => _rows[name];
}
