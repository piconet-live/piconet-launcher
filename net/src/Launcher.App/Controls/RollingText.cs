using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.TextFormatting;

namespace Launcher.App.Controls;

/// <summary>
/// A figure that rolls to its new value. <see cref="TextBlock.Text"/> is the true value at once; only what is drawn
/// passes through the numbers between, so nothing that reads the text ever sees a value in passing. Text that is not one
/// number with the same words around it (a time, a name) changes at once. <see cref="Tick"/>: every change also
/// comes up from a little dim, for a clock.
/// </summary>
public sealed partial class RollingText : TextBlock
{
    public static readonly StyledProperty<bool> TickProperty = AvaloniaProperty.Register<RollingText, bool>(nameof(Tick));

    private string? _shown;

    public bool Tick
    {
        get => GetValue(TickProperty);
        set => SetValue(TickProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override TextLayout CreateTextLayout(string? text) => base.CreateTextLayout(_shown ?? text);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && TopLevel.GetTopLevel(this) is not null)
        {
            Changed(change.OldValue as string, change.NewValue as string);
        }
    }

    private void Changed(string? before, string? after)
    {
        if (Tick && before != after && !string.IsNullOrEmpty(before))
        {
            Motion.Play(this, nameof(Tick), Motion.Quick, Motion.EaseOut, p => Opacity = 0.55 + (0.45 * p),
                () => ClearValue(OpacityProperty));
        }
        if (Number().Match(before ?? "") is not { Success: true } from || Number().Match(after ?? "") is not { Success: true } to
            || from.Groups[1].Value != to.Groups[1].Value || from.Groups[3].Value != to.Groups[3].Value)
        {
            Show(null);
            return;
        }
        var format = Format(to.Groups[2].Value);
        var (start, end) = (Parse(from.Groups[2].Value), Parse(to.Groups[2].Value));
        if (start == end || format is null || Format(from.Groups[2].Value) != format)
        {
            Show(null);
            return;
        }
        var (head, tail) = (to.Groups[1].Value, to.Groups[3].Value);
        Motion.Play(this, nameof(Number), Motion.Standard, Motion.EaseOut,
            p => Show(head + (start + ((end - start) * p)).ToString(format, CultureInfo.InvariantCulture) + tail),
            () => Show(null));
    }

    private void Show(string? shown)
    {
        if (_shown != shown)
        {
            _shown = shown;
            InvalidateTextLayout();
        }
    }

    private static double Parse(string number) => double.Parse(number.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);

    private static string? Format(string number)
    {
        var point = number.IndexOf('.', StringComparison.Ordinal);
        var places = point < 0 ? 0 : number.Length - point - 1;
        return (number.Contains(',', StringComparison.Ordinal) ? "N" : "F") + places.ToString(CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^(\D*?)(-?\d[\d,]*(?:\.\d+)?)(\D*)$")]
    private static partial Regex Number();
}
