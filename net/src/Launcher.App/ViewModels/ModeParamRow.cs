using System.Globalization;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// A mode's <c>param</c> as a row of a lobby's rules panel, read from the compiled package's declaration: a switch its
/// switch, a choice its choices, a whole number its stepper on its steps, and a time or a real number its values
/// dropped down, in words ("30 s"). An optional parameter (<see cref="ModeParam.Or"/>) offers its named value too: a whole
/// number its switch beside the stepper ("By course"), a choice or a switch one choice more. The stats parameter
/// (<see cref="ModeTerms.PackParam"/>) says its <c>stock</c> and <c>file</c> in words. <see cref="Value"/> is the cell a
/// match start's <c>setup.mode.params</c> holds.
/// </summary>
public sealed class ModeParamRow
{
    /// <summary>The most values a dropped-down row lists: a time without a step would list one a game step.</summary>
    private const int MostOffered = 61;

    /// <summary>The values of a real number with no step written.</summary>
    private const int RealSteps = 20;

    private readonly IReadOnlyList<int>? _cells;

    /// <param name="param">The parameter as the package declares it.</param>
    /// <param name="value">Its value now.</param>
    /// <param name="stats">It is the mode's stats parameter.</param>
    public ModeParamRow(ModeParam param, int value, bool stats = false)
    {
        ArgumentNullException.ThrowIfNull(param);
        Param = param;
        Stats = stats;
        (Row, _cells) = param switch
        {
            { Or: { } or, Kind: ModeParamKind.Switch } => ((RuleRow)new ChoiceRule(param.Label, ["Off", "On", or], value), (IReadOnlyList<int>?)null),
            { Kind: ModeParamKind.Switch } => (new SwitchRule(param.Label, value != 0), null),
            { Kind: ModeParamKind.Choice } => (new ChoiceRule(param.Label, [.. Choices(param, stats)], value), null),
            { Or: { } or, Kind: ModeParamKind.Number } => (new NamedNumberRule(param.Label, or, param.StatesNothing(value),
                param.StatesNothing(value) ? param.Default == param.Most ? param.Least : param.Default : value, param.Least, param.Top) { Step = Math.Max(1, param.Step), Unit = param.Unit }, null),
            { Kind: ModeParamKind.Number, Type: ModeType.Int } =>
                (new StepperRule(param.Label, value, param.Least, param.Most) { Step = Math.Max(1, param.Step), Unit = param.Unit }, null),
            _ => Offered(param, value),
        };
    }

    /// <summary>The mode's stats parameter: its <c>stock</c> and <c>file</c> said in words.</summary>
    public bool Stats { get; }

    /// <summary>The stats choice is the host's own pack file now.</summary>
    public bool FileChosen => Stats && Row is ChoiceRule choice && choice.Selected < Param.Choices.Count && Param.Choices[choice.Selected] == ModeTerms.FileChoice;

    /// <summary>A stats parameter's <c>stock</c> as the row says it.</summary>
    public const string StockWords = "Stock";

    /// <summary>A stats parameter's <c>file</c> as the row says it.</summary>
    public const string FileWords = "Your own file";

    /// <summary>A choice parameter's choices in words, its optional name after them.</summary>
    private static IEnumerable<string> Choices(ModeParam param, bool stats) =>
        param.Choices.Select(c => Said(c, stats)).Concat(param.Or is { } or ? [or] : []);

    private static string Said(string choice, bool stats) => !stats ? choice
        : choice == ModeTerms.StockChoice ? StockWords : choice == ModeTerms.FileChoice ? FileWords : choice;

    public ModeParam Param { get; }

    public RuleRow Row { get; }

    /// <summary>The value as the package keeps it (a real's bits).</summary>
    public int Value => Row switch
    {
        SwitchRule on => on.On ? 1 : 0,
        StepperRule stepper => stepper.Value,
        NamedNumberRule named => named.IsNamed ? Param.Most : named.Value,
        ChoiceRule choice when _cells is { } cells => cells[choice.Selected],
        ChoiceRule choice => choice.Selected,
        _ => Param.Default,
    };

    private static (RuleRow, IReadOnlyList<int>) Offered(ModeParam p, int value)
    {
        var range = p with { Most = p.Top, Or = null };
        var cells = Cells(range).Append(p.Default).Append(value).Where(c => !p.StatesNothing(c)).Distinct().OrderBy(c => Sorted(p, c)).ToList();
        if (p.Unset is { } unset)
        {
            cells.Add(unset);   // (the named value last)
        }
        return (new ChoiceRule(p.Label, [.. cells.Select(c => Text(p, c))], cells.IndexOf(value)), cells);
    }

    private static double Sorted(ModeParam p, int cell) => p.Type == ModeType.Real ? BitConverter.Int32BitsToSingle(cell) : cell;

    /// <summary>The range's values along its steps, thinned out to <see cref="MostOffered"/>.</summary>
    private static IEnumerable<int> Cells(ModeParam p)
    {
        if (p.Type == ModeType.Real)
        {
            var (least, most) = (BitConverter.Int32BitsToSingle(p.Least), BitConverter.Int32BitsToSingle(p.Most));
            var step = p.Step == 0 ? (most - least) / RealSteps : BitConverter.Int32BitsToSingle(p.Step);
            var count = step > 0 ? Math.Min(MostOffered - 1, (int)Math.Floor((most - least) / step)) : 0;
            var stride = step > 0 ? Math.Max(step, (most - least) / (MostOffered - 1)) : 0;
            return Enumerable.Range(0, count + 1).Select(i => BitConverter.SingleToInt32Bits(Math.Min(most, least + (i * stride))));
        }
        var size = Math.Max(1, p.Step);
        var steps = (p.Most - (long)p.Least) / size;
        var every = Math.Max(1, (steps + MostOffered - 2) / (MostOffered - 1));
        return Enumerable.Range(0, (int)(steps / every) + 1).Select(i => (int)(p.Least + (i * every * size)));
    }

    /// <summary>A value in words: "30 s", "1.5", "On", a choice's name.</summary>
    public static string Text(ModeParam param, int value, bool stats = false)
    {
        ArgumentNullException.ThrowIfNull(param);
        if (param.StatesNothing(value))
        {
            return param.Or!;
        }
        return param.Kind switch
        {
            ModeParamKind.Switch => value != 0 ? "On" : "Off",
            ModeParamKind.Choice => value >= 0 && value < param.Choices.Count ? Said(param.Choices[value], stats) : "",
            ModeParamKind.Delay => string.Create(CultureInfo.InvariantCulture, $"{value / (double)Steps:0.##} s"),
            _ when param.Type == ModeType.Real => BitConverter.Int32BitsToSingle(value).ToString("0.###", CultureInfo.InvariantCulture),
            _ => param.Unit.Length > 0 ? $"{value.ToString(CultureInfo.InvariantCulture)} {param.Unit}" : value.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The game's steps a second, a delay's unit.</summary>
    private const int Steps = 60;
}
