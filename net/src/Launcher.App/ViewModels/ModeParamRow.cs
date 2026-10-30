using System.Globalization;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// A mode's <c>param</c> as a row of a lobby's rules panel, read from the compiled package's declaration: a switch its
/// switch, a choice its choices, a whole number its stepper on its steps, and a time or a real number its values
/// dropped down, in words ("30 s"). <see cref="Value"/> is the cell a match start's <c>setup.mode.params</c> holds.
/// </summary>
public sealed class ModeParamRow
{
    /// <summary>The most values a dropped-down row lists: a time without a step would list one a game step.</summary>
    private const int MostOffered = 61;

    /// <summary>The values of a real number with no step written.</summary>
    private const int RealSteps = 20;

    private readonly IReadOnlyList<int>? _cells;

    public ModeParamRow(ModeParam param, int value)
    {
        ArgumentNullException.ThrowIfNull(param);
        Param = param;
        (Row, _cells) = param.Kind switch
        {
            ModeParamKind.Switch => ((RuleRow)new SwitchRule(param.Label, value != 0), (IReadOnlyList<int>?)null),
            ModeParamKind.Choice => (new ChoiceRule(param.Label, param.Choices, value), null),
            ModeParamKind.Number when param.Type == ModeType.Int =>
                (new StepperRule(param.Label, value, param.Least, param.Most) { Step = Math.Max(1, param.Step) }, null),
            _ => Offered(param, value),
        };
    }

    public ModeParam Param { get; }

    public RuleRow Row { get; }

    /// <summary>The value as the package keeps it (a real's bits).</summary>
    public int Value => Row switch
    {
        SwitchRule on => on.On ? 1 : 0,
        StepperRule stepper => stepper.Value,
        ChoiceRule choice when _cells is { } cells => cells[choice.Selected],
        ChoiceRule choice => choice.Selected,
        _ => Param.Default,
    };

    private static (RuleRow, IReadOnlyList<int>) Offered(ModeParam p, int value)
    {
        var cells = Cells(p).Append(p.Default).Append(value).Distinct().OrderBy(c => Sorted(p, c)).ToList();
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
    public static string Text(ModeParam param, int value)
    {
        ArgumentNullException.ThrowIfNull(param);
        return param.Kind switch
        {
            ModeParamKind.Switch => value != 0 ? "On" : "Off",
            ModeParamKind.Choice => value >= 0 && value < param.Choices.Count ? param.Choices[value] : "",
            ModeParamKind.Delay => string.Create(CultureInfo.InvariantCulture, $"{value / (double)Steps:0.##} s"),
            _ when param.Type == ModeType.Real => BitConverter.Int32BitsToSingle(value).ToString("0.###", CultureInfo.InvariantCulture),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The game's steps a second, a delay's unit.</summary>
    private const int Steps = 60;
}
