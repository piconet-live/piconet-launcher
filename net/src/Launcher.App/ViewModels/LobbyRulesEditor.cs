using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// A custom lobby's settings as its host sets them, for a new lobby or theirs while it waits: pick a mode, then its
/// settings. The mode is one of the server's official modes (<see cref="LobbyList.Modes"/>: Quick Play's rules first,
/// then those rules with each stat pack, then the others) or the host's own <c>.fzmode</c> file (From file…); its
/// settings are its parameters on their defaults, which the host changes. How the race plays is the mode's (design D10):
/// beside it the host sets only the lobby's own settings (the name, the password, the racers, the ping limit, the vote's
/// and the machine choice's times, the voting minimum), the teams within the mode's word, and the race's rules that have
/// no mode word yet (<see cref="LobbyRules.NoModeWordYet"/>: the CPU racers, the course; the host's own stats file).
/// <see cref="Rules"/> holds only the lobby settings that differ from Quick Play's: a missing one is Quick Play's.
/// </summary>
public sealed partial class LobbyRulesEditor : ObservableObject
{
    /// <summary>The stats row's first choice: the mode's stats (its <c>stats</c> word, or Quick Play's pack).</summary>
    public const string ModesStats = "Mode's";

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
    /// <param name="file">The host's own stat pack file sent last (<see cref="QuickPlaySession.HostPack"/>): offered
    /// in the stats row, and the choice when the lobby's rules name it.</param>
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
        TeamsRow = NewTeamsRow(_start.Value(LobbyRules.TeamsKey) ?? 0);
        _params = NewParamRows();
        foreach (var rule in CustomRules.HostGroups.SelectMany(g => g.Rules))
        {
            var row = rule == HostRules.StatPack
                ? new PackRule(rule.Label, [ModesStats], file is not null && _start.StatPack == file.Name ? 1 : 0, file)
                : HostRuleRows.ForLobby(rule, _start, _quickPlay, null, null, ModeCourses);
            row.PropertyChanged += OnRowChanged;
            _rows[rule.Name] = row;
        }
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

    /// <summary>The lobby's teams under its mode: None, or a number the mode's word allows.</summary>
    public ChoiceRule TeamsRow { get; private set; }

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
        IReadOnlyList<ModeParamRow> rows = [.. declared.Select((p, i) =>
            new ModeParamRow(p, values is { } v && v.Count == declared.Count ? v[i] : p.Default))];
        foreach (var row in rows)
        {
            row.Row.PropertyChanged += OnRowChanged;
        }
        return rows;
    }

    private ChoiceRule NewTeamsRow(int value)
    {
        var limit = ModeRow.FileChosen ? ModeRow.File!.Terms.Teams : null;
        var numbers = Enumerable.Range(limit?.Least ?? ModeTeams.Least, (limit?.Most ?? ModeTeams.Most) - (limit?.Least ?? ModeTeams.Least) + 1)
            .Select(n => n.ToString(CultureInfo.InvariantCulture));
        string[] options = limit is null ? [NoTeams, .. numbers] : [.. numbers];
        var row = new ChoiceRule("Teams", options, Math.Max(0, Array.IndexOf(options, value.ToString(CultureInfo.InvariantCulture))));
        row.PropertyChanged += OnRowChanged;
        return row;
    }

    private const string NoTeams = "None";

    /// <summary>The rules the chosen mode owns, by their keys: their rows do not show (the stats row when it names a pack).</summary>
    private HashSet<string> Owned()
    {
        var owned = ModeRow.FileChosen ? ModeRow.File!.Terms.Rules().Keys : ModeRow.Named?.Terms?.Keys;
        var pack = ModeRow.FileChosen ? ModeRow.File!.Terms.Pack : ModeRow.Named?.Pack;
        return new HashSet<string>([.. owned ?? [], .. pack is null ? [] : new[] { HostRules.StatPack.Name }], StringComparer.Ordinal);
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
        RuleRow[] teams = ModeRow.Any ? [TeamsRow] : [];
        RuleRow[] settings = [.. teams, .. _params.Select(p => p.Row)];
        // the create face: the mode's settings under its name; none: a line that says so
        CreateGroups = [new RuleGroup(first), new RuleGroup(settings.Length > 0 ? settings : [new ValueRule("Settings", "None")])
        {
            Later = true,
            Heading = ModeRow.Chosen?.Name ?? ModeRow.Label,
        }];
        LobbyGroups = RuleGroup.Of(first, [ModeRow, .. settings]);
        var owned = Owned();
        RuleGroups = [.. CustomRules.HostGroups.Select(g => (g.Heading, Rows: g.Rules.Where(r => !owned.Contains(r.Name)).Select(r => _rows[r.Name]).ToList()))
            .Where(g => g.Rows.Count > 0).Select((g, i) => new RuleGroup(g.Rows)
            {
                Later = i > 0,
                Heading = g.Heading,
            })];
    }

    /// <summary>The settings as the rows hold them now: the lobby settings that differ from Quick Play's, the teams, the
    /// host's own stats file, and the mode with its settings (an official or the lobby's by its hash, a new file whole).</summary>
    public LobbyRules Rules
    {
        get
        {
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rule in CustomRules.HostGroups.SelectMany(g => g.Rules).Where(r => r != HostRules.StatPack))
            {
                HostRuleRows.Write(rule, _rows[rule.Name], values);
            }
            var against = _quickPlay ?? CustomRules.Defaults;
            var differing = values.Where(p => against.Value(p.Key) != p.Value).ToDictionary(p => p.Key, p => p.Value);
            if (values.TryGetValue(LobbyRules.FixedCourseKey, out var course))
            {
                (differing[HostRules.CourseSelect.Name], differing[LobbyRules.FixedCourseKey]) = ((int)CourseSelectMode.Fixed, course);
            }
            if (ModeRow.Any && int.TryParse(TeamsRow.Chosen, CultureInfo.InvariantCulture, out var teams))
            {
                differing[LobbyRules.TeamsKey] = teams;
            }
            // the host's own stats file, unless the mode names the pack (a pack by name is the mode's word)
            var pack = _rows[HostRules.StatPack.Name];
            var file = Owned().Contains(HostRules.StatPack.Name) ? null : HostRuleRows.PackFile(pack);
            return new LobbyRules(differing, file is null ? null : HostRuleRows.Pack(pack), file,
                ModeRow.Named is { } named ? named with { Values = ModeValues } : null, ModeRow.FileChosen ? ModeRow.File!.Text : null, ModeValues);
        }
    }

    /// <summary>The settings in one line, the mode first: its name, its settings moved from their defaults, then the
    /// lobby settings that differ from Quick Play's.</summary>
    public string Summary => Said(Rules) is var rules && ModeRow.FileChosen && ModeRow.File is { } file
        ? string.Join(" · ", new[] { file.Name, CustomRules.ParamsLine(file.Params, ModeValues), CustomRules.CoursesLine(file.Terms.Courses), CustomRules.MachinesLine(file.Terms.Machines), CustomRules.CustomsLine(file.Terms.CustomsOff), CustomRules.Summary(rules with { Mode = null, ModeValues = null }, _quickPlay) }.Where(s => s.Length > 0))
        : CustomRules.Summary(rules, _quickPlay);

    /// <summary>The rules as the summary reads them: no pack of the host's is Quick Play's (what the server races when
    /// the mode names none), not the disc's own stats.</summary>
    private LobbyRules Said(LobbyRules rules) => rules.StatPack is null ? rules with { StatPack = _quickPlay?.StatPack } : rules;

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
            // the mode's settings come, its teams' range, its courses; the rows it owns go
            TeamsRow.PropertyChanged -= OnRowChanged;
            TeamsRow = NewTeamsRow(int.TryParse(TeamsRow.Chosen, CultureInfo.InvariantCulture, out var teams) ? teams : 0);
            _params = NewParamRows();
            NewCourseRow();
            Regroup();
            OnPropertyChanged(nameof(ParamRows));
        }
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>A row by its rule's name.</summary>
    public RuleRow Rule(string name) => _rows[name];
}
