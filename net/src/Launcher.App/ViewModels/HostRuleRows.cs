using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// A host's rule as a rules panel's row (Practice is a custom lobby's rules panel for one player): the label, the range
/// and the control come from the rule in the one list (<see cref="HostRules"/>), so a practice and a custom lobby show
/// a rule alike. Laps get their stepper (and Unlimited), the machine setting its bar, a switch its switch, the mines'
/// return its milliseconds with Off, the stat pack the packs by name.
/// </summary>
public static class HostRuleRows
{
    /// <summary>The stat pack's choice for none: the disc's own stats.</summary>
    public const string Stock = "Stock";

    /// <summary>A rule's row, holding <paramref name="value"/> (a switch: not 0 is on; a delay: 0 is off).</summary>
    /// <param name="packs">The stat packs to choose from, by name (the stat pack's rule only).</param>
    /// <param name="pack">The pack chosen; null or one not offered: <see cref="Stock"/>.</param>
    public static RuleRow For(HostRule rule, int value, IReadOnlyList<string>? packs = null, string? pack = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Kind switch
        {
            // the lap counter's laps: 0, the game's free run, is the Unlimited switch beside the number
            RuleKind.Number when rule == HostRules.Laps => new LapsRule(rule.Label, value, rule.Most) { Unlimited = value == 0 },
            // (a range's one value here: the player's own, as a practice has it)
            RuleKind.Range => new SliderRule(rule.Label, value, rule.Least, rule.Most),
            RuleKind.Number => new StepperRule(rule.Label, value, rule.Least, rule.Most),
            RuleKind.Switch => new SwitchRule(rule.Label, value != 0),
            RuleKind.Delay => new DelayRule(rule.Label, value, rule.Least, rule.Most, rule.Step, rule.Default),
            RuleKind.Pack => new ChoiceRule(rule.Label, [Stock, .. packs ?? []],
                pack is null ? 0 : Math.Max(0, (packs ?? []).ToList().IndexOf(pack) + 1)),
            _ => throw new NotSupportedException($"no row yet for the rule {rule.Name} ({rule.Kind})"),
        };
    }

    /// <summary>A row's value as its rule's number (a switch: 1 on; a delay: 0 off; laps: 0 unlimited).</summary>
    public static int Number(RuleRow row) => row switch
    {
        LapsRule laps => laps.Laps,
        SliderRule slider => slider.Whole,
        StepperRule stepper => stepper.Value,
        SwitchRule on => on.On ? 1 : 0,
        DelayRule delay => delay.Ms,
        _ => throw new NotSupportedException($"the row {row?.Label} holds no number"),
    };

    /// <summary>The stat pack a pack's row holds: null for <see cref="Stock"/>.</summary>
    public static string? Pack(RuleRow row) => row is ChoiceRule { Selected: > 0 } choice ? choice.Chosen : null;

    /// <summary>The CPU racers' choices, in <see cref="CpuFillMode"/>'s order.</summary>
    public static IReadOnlyList<string> CpuFillChoices { get; } = ["Vote", "Always", "Never"];

    /// <summary>A custom lobby's rule as its host's row, holding the lobby's value (<see cref="CustomRules.Value"/>):
    /// a range its two ends, the CPU racers their choices side by side, the course its choices and its fixed course, a lobby's time its number;
    /// the others as <see cref="For"/> gives them.</summary>
    public static RuleRow ForLobby(HostRule rule, LobbyRules? rules, LobbyRules? quickPlay, IReadOnlyList<string>? packs)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var value = CustomRules.Value(rules, rule.Name, quickPlay);
        return rule.Kind switch
        {
            RuleKind.Range => new RangeRule(rule.Label, value, CustomRules.Value(rules, LobbyRules.SettingHighKey, quickPlay), rule.Least, rule.Most),
            RuleKind.Choice when rule == HostRules.CpuFill => new SegmentedRule(rule.Label, CpuFillChoices, value),
            RuleKind.Course => new CourseRule(rule.Label, value, CustomRules.Value(rules, LobbyRules.FixedCourseKey, quickPlay)),
            RuleKind.Text => new StepperRule(rule.Label, value, rule.Least, rule.Most),
            RuleKind.Pack => For(rule, 0, packs, rules?.StatPack),
            _ => For(rule, value, packs),
        };
    }

    /// <summary>A custom lobby's row's value into the rules' values (<see cref="ForLobby"/>'s form): a range's two
    /// keys, a fixed course its course too; a pack's goes in the rules' own member (<see cref="Pack"/>).</summary>
    public static void Write(HostRule rule, RuleRow row, IDictionary<string, int> values)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(values);
        switch (row)
        {
            case RangeRule range:
                (values[rule.Name], values[LobbyRules.SettingHighKey]) = (range.Low, range.High);
                break;
            case CourseRule course:
                values[rule.Name] = course.Selected;
                if (course.IsFixed)
                {
                    values[LobbyRules.FixedCourseKey] = course.Course;
                }
                break;
            case SegmentedRule segmented:
                values[rule.Name] = segmented.Selected;
                break;
            case ChoiceRule:
                break;   // (the stat pack)
            default:
                values[rule.Name] = Number(row);
                break;
        }
    }
}
