using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// Play's Practice page: solo, alone, no CPU racers, in Practice's game mode (<see cref="PracticeMode"/>). The courses
/// on the left (one picked), the set-up on the right as a custom lobby's rules panel for one player: the machine with
/// the Garage, its stats and its setting; under "Practice" the mode's parameters, each the row a custom lobby's host
/// gets for it (<see cref="ModeParamRow"/>; a parameter added to the mode file is a row more), then the rewind's reach
/// and the save's buttons as values; under "Race rules" the host's other rules that mean something alone
/// (<see cref="HostRules.Alone"/>), the rows a lobby shows (<see cref="HostRuleRows"/>). A rule the mode states is the
/// mode's and has no row. START PRACTICE. It opens on the practice last started, else the mode's defaults and Quick
/// Play's rules. Labels and values only.
/// </summary>
public sealed partial class PracticeViewModel : ViewModelBase
{
    /// <summary>The section of the mode's parameters.</summary>
    public const string ModeHeading = "Practice";

    /// <summary>The section of the host's rules beside the mode.</summary>
    public const string RulesHeading = "Race rules";

    /// <summary>The host's rules shown with the machine.</summary>
    private static readonly HostRule[] MachineRules = [HostRules.StatPack, HostRules.MachineSetting];

    /// <summary>The host's rules of the race that mean something alone and that a practice's set-up holds; the laps and
    /// Restore are the mode's (its parameters).</summary>
    private static readonly HostRule[] RaceRules = [HostRules.BoostStart, HostRules.MinesRespawn];

    private readonly MachineRule _machine;
    private readonly Dictionary<string, RuleRow> _rules;
    private readonly Func<PracticeSetup, string?>? _start;
    private readonly Func<RacedMachine> _machineOf;

    /// <param name="machine">The machine the player races as the Garage has it now (<see cref="RacerProfile.Raced"/>):
    /// a custom machine when one is raced.</param>
    /// <param name="garage">Shows the Garage.</param>
    /// <param name="packs">The stat packs a practice can run on, by name, after the disc's own.</param>
    /// <param name="mode">Practice's game mode (<see cref="AppState.PracticeModeFile"/>); null: none could be read,
    /// and there are no rows of it.</param>
    /// <param name="start">Starts the game on a set-up: null when it started, else the notice; null: nothing to
    /// start.</param>
    /// <param name="last">The practice last started (the settings'): the page opens on its course and rules; null:
    /// <see cref="PracticeSetup.Default"/>.</param>
    public PracticeViewModel(Func<RacedMachine> machine, Action garage, IReadOnlyList<string> packs, ModePackage? mode,
                             Func<PracticeSetup, string?>? start = null, PracticeSetup? last = null)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(packs);
        _machineOf = machine;
        _start = start;
        var opened = (last ?? PracticeSetup.Default).Kept();
        Courses = new CoursePickerModel(PickMode.One, [opened.Course]);
        _machine = new MachineRule("Machine", NameOf(machine()), garage);
        bool HasRow(HostRule rule) => mode is null || !PracticeMode.States(mode, rule);
        _rules = MachineRules.Concat(RaceRules).Where(HasRow)
            .ToDictionary(r => r.Name, r => HostRuleRows.For(r, ValueOf(r, opened), packs, opened.StatPack));
        var values = mode is null ? [] : PracticeMode.Values(mode, opened);
        var stats = mode is null ? null : ModeTerms.Of(mode, values).PackParam;
        Params = mode is null ? [] : [.. mode.Params.Select((p, i) => new ModeParamRow(p, values[i], i == stats))];
        List<RuleRow> fixedWords = [];
        if (mode is not null && PracticeMode.RewindSeconds(mode, values) is { } rewind)
        {
            fixedWords.Add(new ValueRule("Rewind", Clock(rewind)));
        }
        RuleRow[] practice = [.. Params.Select(p => p.Row), .. fixedWords];
        RuleRow[] race = [.. RaceRules.Where(r => _rules.ContainsKey(r.Name)).Select(r => _rules[r.Name])];
        Groups = [.. new (string? Heading, RuleRow[] Rows)[]
            {
                (null, [_machine, .. MachineRules.Where(r => _rules.ContainsKey(r.Name)).Select(r => _rules[r.Name])]),
                (ModeHeading, practice),
                (RulesHeading, race),
            }.Where(g => g.Rows.Length > 0).Select((g, i) => new RuleGroup(g.Rows) { Later = i > 0, Heading = g.Heading })];
    }

    /// <summary>A rule's number in a set-up (<see cref="HostRuleRows.Number"/>'s form).</summary>
    private static int ValueOf(HostRule rule, PracticeSetup s) => rule.Name switch
    {
        "boost-start" => s.BoostFromStart ? 1 : 0,
        "mine-regen" => s.MineRegenMs,
        "machine-setting" => s.MachineSetting,
        "stat-pack" => 0,
        _ => throw new NotSupportedException($"a practice has no {rule.Name}"),
    };

    /// <summary>A rule's row's number; the rule's own default when the mode states it (no row).</summary>
    private int Number(HostRule rule) => _rules.TryGetValue(rule.Name, out var row) ? HostRuleRows.Number(row) : rule.Default;

    public CoursePickerModel Courses { get; }

    public IReadOnlyList<RuleGroup> Groups { get; }

    /// <summary>The rows of the mode's parameters, in the mode file's order.</summary>
    public IReadOnlyList<ModeParamRow> Params { get; }

    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The set-up as the page shows it now.</summary>
    public PracticeSetup Setup
    {
        get
        {
            var raced = _machineOf();
            return new PracticeSetup(
                Courses.Picked, raced.Machine, _rules.TryGetValue(HostRules.StatPack.Name, out var pack) ? HostRuleRows.Pack(pack) : null,
                Number(HostRules.MachineSetting), Number(HostRules.BoostStart) != 0, Number(HostRules.MinesRespawn),
                new ParamDictionary(Params.Select(p => KeyValuePair.Create(p.Param.Name, p.Value))), raced.Custom).Kept();
        }
    }

    /// <summary>A rule's row by the rule's name (<see cref="HostRules"/>).</summary>
    public RuleRow Rule(string name) => _rules[name];

    /// <summary>A mode parameter's row by the parameter's name in the mode file.</summary>
    public RuleRow Param(string name) => Params.Single(p => p.Param.Name == name).Row;

    /// <summary>The page shows: the machine is the Garage's as it is now.</summary>
    public void Shown() => _machine.Value = NameOf(_machineOf());

    /// <summary>The Machine row's value: the raced machine's name, a custom machine's by the game's rule (again once
    /// the disc's tables are read).</summary>
    private string NameOf(RacedMachine raced) => Theme.GameSkin.NameOf(raced, () => OnUi(Shown));

    private string? _blocked;

    private bool CanStart => _start is not null && _blocked is null;

    /// <summary>No game starts now (<see cref="AppState.PipelinesBlock"/>): START PRACTICE is off and the line under it
    /// says why; null: it starts again.</summary>
    public void Block(string? why)
    {
        if (why == _blocked)
        {
            return;
        }
        var before = _blocked;
        _blocked = why;
        StartCommand.NotifyCanExecuteChanged();
        if (why is not null)
        {
            Notice = why;
        }
        else if (Notice == before)
        {
            Notice = "";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => Notice = _start?.Invoke(Setup) ?? "";

    /// <summary>The series' way: 1'00" (<see cref="ReplayWords.Seconds"/>).</summary>
    public static string Clock(int seconds) => ReplayWords.Seconds(seconds);
}
