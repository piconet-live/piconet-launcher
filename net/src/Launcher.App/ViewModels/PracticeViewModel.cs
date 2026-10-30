using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// Play's Practice page: solo, alone, no CPU racers, in
/// the game's own Practice mode. The courses on the left (one picked), the set-up on the right: a custom lobby's
/// rules panel for one player: the machine with the Garage, then every host's rule that
/// means something alone (<see cref="HostRules.Alone"/>: the stats and the machine setting with the machine, then
/// the race's (laps are Practice's mode file's, unlimited): boosting from the start, Restore, the mines' return), as rows of the one list
/// (<see cref="HostRuleRows"/>); the rewind's reach and the save's buttons as values; START PRACTICE. It opens on
/// the practice last started, else Quick Play's rules. Labels and values only.
/// </summary>
public sealed partial class PracticeViewModel : ViewModelBase
{
    /// <summary>The rules shown with the machine; the other rules alone are the race's.</summary>
    private static readonly HostRule[] MachineRules = [HostRules.StatPack, HostRules.MachineSetting];

    private readonly MachineRule _machine;
    private readonly Dictionary<string, RuleRow> _rules;
    private readonly Func<PracticeSetup, string?>? _start;
    private readonly Func<RacedMachine> _machineOf;

    /// <param name="machine">The machine the player races as the Garage has it now (<see cref="RacerProfile.Raced"/>):
    /// a custom machine when one is raced.</param>
    /// <param name="garage">Shows the Garage.</param>
    /// <param name="packs">The stat packs a practice can run on, by name, after the disc's own.</param>
    /// <param name="start">Starts the game on a set-up: null when it started, else the notice; null: nothing to
    /// start.</param>
    /// <param name="last">The practice last started (the settings'): the page opens on its course and rules; null:
    /// <see cref="PracticeSetup.Default"/>.</param>
    public PracticeViewModel(Func<RacedMachine> machine, Action garage, IReadOnlyList<string> packs, Func<PracticeSetup, string?>? start = null,
                             PracticeSetup? last = null)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(packs);
        _machineOf = machine;
        _start = start;
        var opened = (last ?? PracticeSetup.Default).Kept();
        Courses = new CoursePickerModel(PickMode.One, [opened.Course]);
        _machine = new MachineRule("Machine", NameOf(machine()), garage);
        _rules = HostRules.Alone.ToDictionary(r => r.Name, r => HostRuleRows.For(r, ValueOf(r, opened), packs, opened.StatPack));
        Groups = RuleGroup.Of(
            [_machine, .. MachineRules.Select(r => _rules[r.Name])],
            [.. HostRules.Alone.Except(MachineRules).Where(r => r != HostRules.Laps).Select(r => _rules[r.Name])],
            [new ValueRule("Rewind", Clock(PracticeSetup.RewindSeconds)), new ValueRule("Save · Load", "D-pad right · left")]);
    }

    /// <summary>A rule's number in a set-up (<see cref="HostRuleRows.Number"/>'s form).</summary>
    private static int ValueOf(HostRule rule, PracticeSetup s) => rule.Name switch
    {
        "laps" => s.Laps,
        "boost-start" => s.BoostFromStart ? 1 : 0,
        "restore" => s.Restore ? 1 : 0,
        "mine-regen" => s.MineRegenMs,
        "machine-setting" => s.MachineSetting,
        "stat-pack" => 0,
        _ => throw new NotSupportedException($"a practice has no {rule.Name}"),
    };

    private int Number(HostRule rule) => HostRuleRows.Number(_rules[rule.Name]);

    public CoursePickerModel Courses { get; }

    public IReadOnlyList<RuleGroup> Groups { get; }

    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The set-up as the page shows it now.</summary>
    public PracticeSetup Setup
    {
        get
        {
            var raced = _machineOf();
            return new PracticeSetup(
                Courses.Picked, raced.Machine, HostRuleRows.Pack(_rules[HostRules.StatPack.Name]), Number(HostRules.MachineSetting),
                Number(HostRules.Laps), Number(HostRules.Restore) != 0, Number(HostRules.BoostStart) != 0, Number(HostRules.MinesReturn),
                raced.Custom).Kept();
        }
    }

    /// <summary>A rule's row by the rule's name (<see cref="HostRules"/>).</summary>
    public RuleRow Rule(string name) => _rules[name];

    /// <summary>The page shows: the machine is the Garage's as it is now.</summary>
    public void Shown() => _machine.Value = NameOf(_machineOf());

    /// <summary>The Machine row's value: the raced machine's name, a custom machine's by the game's rule (again once
    /// the disc's tables are read).</summary>
    private string NameOf(RacedMachine raced) => Theme.GameSkin.NameOf(raced, () => OnUi(Shown));

    private bool CanStart => _start is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => Notice = _start?.Invoke(Setup) ?? "";

    /// <summary>The series' way: 1'00" (<see cref="ReplayWords.Seconds"/>).</summary>
    public static string Clock(int seconds) => ReplayWords.Seconds(seconds);
}
