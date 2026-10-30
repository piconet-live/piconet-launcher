using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// A custom lobby's settings as its host sets them, for a new lobby or theirs while it waits: the name, the password,
/// a preset to start from (Quick Play's rules, or Quick Play's with one of the stat packs), and every rule a host
/// sets (<see cref="LobbyRules.Settable"/>) as a rules panel's rows in their groups (<see cref="CustomRules.Groups"/>).
/// <see cref="Rules"/> holds only the rules that differ from Quick Play's: a missing rule is Quick Play's.
/// </summary>
public sealed partial class LobbyRulesEditor : ObservableObject
{
    /// <summary>The first preset: Quick Play's rules.</summary>
    public const string QuickPlayPreset = "Quick Play rules";

    private readonly LobbyRules? _quickPlay;
    private readonly LobbyRules _start;
    private readonly IReadOnlyList<string> _packs;
    private readonly Dictionary<string, RuleRow> _rows = [];
    private HostPackFile? _file;
    private bool _applying;

    /// <param name="start">The rules the panel opens on (a lobby's own; null: Quick Play's).</param>
    /// <param name="quickPlay">The server's Quick Play rules (null: the rules' defaults).</param>
    /// <param name="packs">The stat packs, by name.</param>
    /// <param name="name">The lobby's name.</param>
    /// <param name="locked">The lobby has a password now (editing a lobby: the switch that keeps or clears it).</param>
    /// <param name="editing">The host's own lobby (not a new one): the password is kept unless changed.</param>
    /// <param name="file">The host's own stat pack file sent last (<see cref="QuickPlaySession.HostPack"/>): offered
    /// with the packs, and the choice when the lobby's rules name it.</param>
    public LobbyRulesEditor(LobbyRules? start, LobbyRules? quickPlay, IReadOnlyList<string> packs, string name, bool locked = false,
        bool editing = false, HostPackFile? file = null)
    {
        ArgumentNullException.ThrowIfNull(packs);
        (_quickPlay, Editing, _file) = (quickPlay, editing, file);
        // Quick Play's own pack is offered whatever this launcher holds; a new lobby opens on Quick Play's rules
        // whole, its pack too (no pack is the disc's own stats, not Quick Play's)
        _packs = quickPlay?.StatPack is { } served && !packs.Contains(served) ? [served, .. packs] : packs;
        _start = start ?? QuickPlayStart;
        NameRow = new TextRule("Name", "Lobby name", CustomLobby.MaxName) { Text = name };
        PasswordRow = editing
            ? new TextRule("New password", locked ? "Unchanged" : "None", CustomLobby.MaxPassword) { Secret = true }
            : new TextRule("Password", "None", CustomLobby.MaxPassword) { Secret = true };
        LockedRow = new SwitchRule("Password", locked);
        Presets = [QuickPlayPreset, .. _packs.Select(p => p + " stats")];
        PresetRow = new PresetRule("Preset", Presets, _start.StatPack is { } pack && pack != quickPlay?.StatPack && _packs.Contains(pack)
            ? _packs.ToList().IndexOf(pack) + 1 : 0) { Toggles = !editing };
        PresetRow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PresetRule.Selected))
            {
                Apply(PresetRow.Selected);
            }
        };
        ModeRow = new GameModeRule("Mode", _start.Mode);
        ModeRow.PropertyChanged += OnRowChanged;
        TeamsRow = NewTeamsRow(_start.Value(LobbyRules.TeamsKey) ?? 0);
        RuleGroups = Build(_start);
        PresetRow.Note = SummaryShown;
        LobbyGroups = MakeLobbyGroups();
    }

    /// <summary>The name, the password, and the mode with its teams and the preset.</summary>
    private IReadOnlyList<RuleGroup> MakeLobbyGroups()
    {
        RuleRow[] first = Editing ? [NameRow, LockedRow, PasswordRow] : [NameRow, PasswordRow];
        return ModeChosen ? RuleGroup.Of(first, [ModeRow, TeamsRow, PresetRow]) : RuleGroup.Of(first, [ModeRow, PresetRow]);
    }

    /// <summary>The lobby's teams under its mode: None, or a number the mode's word allows.</summary>
    public ChoiceRule TeamsRow { get; private set; }

    /// <summary>A mode is the choice (the host's file, or the one the lobby has).</summary>
    private bool ModeChosen => ModeRow.FileChosen || ModeRow.ServedChosen;

    private ChoiceRule NewTeamsRow(int value)
    {
        var limit = ModeRow.FileChosen ? ModeRow.File!.Terms.Teams : null;
        var numbers = Enumerable.Range(limit?.Least ?? ModeTeams.Least, (limit?.Most ?? ModeTeams.Most) - (limit?.Least ?? ModeTeams.Least) + 1)
            .Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string[] options = limit is null ? [NoTeams, .. numbers] : [.. numbers];
        var row = new ChoiceRule("Teams", options, Math.Max(0, Array.IndexOf(options, value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        row.PropertyChanged += OnRowChanged;
        return row;
    }

    private const string NoTeams = "None";

    /// <summary>The rules the chosen mode owns, by their keys: their rows do not show.</summary>
    private HashSet<string> Owned()
    {
        var owned = ModeRow.FileChosen ? ModeRow.File!.Terms.Rules().Keys : ModeRow.ServedChosen ? ModeRow.Served!.Terms?.Keys : null;
        var pack = ModeRow.FileChosen ? ModeRow.File!.Terms.Pack : ModeRow.ServedChosen ? ModeRow.Served!.Pack : null;
        return new HashSet<string>([.. owned ?? [], .. pack is null ? [] : new[] { HostRules.StatPack.Name }], StringComparer.Ordinal);
    }

    /// <summary>The host's own lobby: the password row changes it (empty: kept), the switch clears it.</summary>
    public bool Editing { get; }

    public TextRule NameRow { get; }

    public TextRule PasswordRow { get; }

    /// <summary>While editing: the lobby keeps a password (off: none).</summary>
    public SwitchRule LockedRow { get; }

    /// <summary>The preset, with the rules' line under it and (a new lobby) the toggle that shows every rule.</summary>
    public PresetRule PresetRow { get; }

    /// <summary>The lobby's game mode: none, the one it has, or the host's own file (Choose file).</summary>
    public GameModeRule ModeRow { get; }

    /// <summary>How the course is chosen, with the fixed course and its picker.</summary>
    public CourseRule CourseRow => (CourseRule)_rows[HostRules.CourseSelect.Name];

    /// <summary>The course picker shows in place of the rules.</summary>
    public bool Picking => CourseRow.Picking;

    public IReadOnlyList<string> Presets { get; }

    /// <summary>The name, the password, the mode and the preset.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RuleGroup> LobbyGroups { get; private set; } = [];

    /// <summary>Every rule, in its groups (with headings).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RuleGroup> RuleGroups { get; private set; } = [];

    /// <summary>Quick Play's rules as a new lobby starts on them: none of the host's own, Quick Play's pack.</summary>
    private LobbyRules QuickPlayStart => new(new Dictionary<string, int>(), _quickPlay?.StatPack);

    /// <summary>The rules as the rows hold them now: only those that differ from Quick Play's, and the stat pack
    /// always (none: the disc's own stats).</summary>
    public LobbyRules Rules
    {
        get
        {
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rule in LobbyRules.Settable)
            {
                HostRuleRows.Write(rule, _rows[rule.Name], values);
            }
            var against = _quickPlay ?? CustomRules.Defaults;
            var differing = values.Where(p => against.Value(p.Key) != p.Value).ToDictionary(p => p.Key, p => p.Value);
            // a range's two ends go together, as a fixed course with its course
            if (differing.ContainsKey(HostRules.MachineSetting.Name) || differing.ContainsKey(LobbyRules.SettingHighKey))
            {
                (differing[HostRules.MachineSetting.Name], differing[LobbyRules.SettingHighKey]) =
                    (values[HostRules.MachineSetting.Name], values[LobbyRules.SettingHighKey]);
            }
            if (values.TryGetValue(LobbyRules.FixedCourseKey, out var course))
            {
                (differing[HostRules.CourseSelect.Name], differing[LobbyRules.FixedCourseKey]) = ((int)CourseSelectMode.Fixed, course);
            }
            if (ModeChosen && int.TryParse(TeamsRow.Chosen, System.Globalization.CultureInfo.InvariantCulture, out var teams))
            {
                differing[LobbyRules.TeamsKey] = teams;
            }
            var pack = _rows[HostRules.StatPack.Name];
            // the mode: a new file's text, or the lobby's as the server named it (kept), or none
            return new LobbyRules(differing, HostRuleRows.Pack(pack), HostRuleRows.PackFile(pack),
                ModeRow.ServedChosen ? ModeRow.Served : null, ModeRow.FileChosen ? ModeRow.File!.Text : null);
        }
    }

    /// <summary>The rules that differ from Quick Play's, in one line ("" for Quick Play's own), the mode first.</summary>
    public string Summary => ModeRow.FileChosen && ModeRow.File is { } file
        ? string.Join(" · ", new[] { file.Name, CustomRules.Summary(Rules with { Mode = null }, _quickPlay) }.Where(s => s.Length > 0))
        : CustomRules.Summary(Rules, _quickPlay);

    /// <summary>The line under the preset: the differing rules, or that they are Quick Play's.</summary>
    public string SummaryShown => Summary is { Length: > 0 } s ? s : "Same as Quick Play";

    /// <summary>The name as a request sends it: trimmed.</summary>
    public string Name => NameRow.Text.Trim();

    /// <summary>The name can be sent.</summary>
    public bool NameValid => Name.Length is > 0 and <= CustomLobby.MaxName;

    private List<RuleGroup> Build(LobbyRules? rules)
    {
        foreach (var row in _rows.Values)
        {
            row.PropertyChanged -= OnRowChanged;
        }
        if (_rows.GetValueOrDefault(HostRules.StatPack.Name) is PackRule { File: { } chosen })
        {
            _file = chosen;   // (a preset chosen after a file: the file stays offered)
        }
        _rows.Clear();
        foreach (var rule in LobbyRules.Settable)
        {
            var row = HostRuleRows.ForLobby(rule, rules, _quickPlay, _packs, _file);
            row.PropertyChanged += OnRowChanged;
            _rows[rule.Name] = row;
        }
        OnPropertyChanged(nameof(CourseRow));
        OnPropertyChanged(nameof(Picking));
        return Shown();
    }

    /// <summary>The rows in their groups, but those the mode owns.</summary>
    private List<RuleGroup> Shown()
    {
        var owned = Owned();
        return [.. CustomRules.Groups.Select(g => (g.Heading, Rows: g.Rules.Where(r => !owned.Contains(r.Name)).Select(r => _rows[r.Name]).ToList()))
            .Where(g => g.Rows.Count > 0).Select((g, i) => new RuleGroup(g.Rows)
            {
                Later = i > 0,
                Heading = g.Heading,
            })];
    }

    /// <summary>A preset chosen: every rule back to Quick Play's, with the preset's stat pack.</summary>
    private void Apply(int preset)
    {
        if (_applying)
        {
            return;
        }
        _applying = true;
        try
        {
            var pack = preset > 0 && preset <= _packs.Count ? _packs[preset - 1] : _quickPlay?.StatPack;
            RuleGroups = Build(new LobbyRules(new Dictionary<string, int>(), pack));
            Changed();
        }
        finally
        {
            _applying = false;
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CourseRule.Picking))
        {
            OnPropertyChanged(nameof(Picking));
            return;
        }
        if (sender == ModeRow && e.PropertyName is nameof(GameModeRule.Selected) or nameof(GameModeRule.Options))
        {
            // the mode's rows go, its teams' range comes, the teams' row shows only with a mode
            TeamsRow.PropertyChanged -= OnRowChanged;
            TeamsRow = NewTeamsRow(int.TryParse(TeamsRow.Chosen, System.Globalization.CultureInfo.InvariantCulture, out var teams) ? teams : 0);
            RuleGroups = Shown();
            LobbyGroups = MakeLobbyGroups();
        }
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SummaryShown));
        PresetRow.Note = SummaryShown;
    }

    /// <summary>A row by its rule's name.</summary>
    public RuleRow Rule(string name) => _rows[name];
}
