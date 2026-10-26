using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Launcher.App.Theme;

namespace Launcher.App.Controls;

/// <summary>
/// The builder's labels on its 3D machine: each part's kind and name at a fixed place in the view, never under the
/// name's band at its top (<see cref="Clear"/>: in a small window the name would run over the cockpit's label): the
/// cockpit at the right under that band, the body low on the left, the booster low on the right, a thin line from
/// each to its
/// part as the machine turns (<see cref="Anchors"/>, from the preview's <see cref="MachinePreview.AnchorsMoved"/>),
/// and a dot on the part. Drawn by the toolkit over the view; it takes no pointer, so the machine is still turned
/// beneath it.
/// </summary>
public sealed class PartCallouts : Control
{
    public static readonly StyledProperty<IReadOnlyList<string>?> NamesProperty =
        AvaloniaProperty.Register<PartCallouts, IReadOnlyList<string>?>(nameof(Names));

    /// <summary>How far down the view the machine's name, its letters and the pilot stand: no label above it.</summary>
    public static readonly StyledProperty<double> ClearProperty =
        AvaloniaProperty.Register<PartCallouts, double>(nameof(Clear));

    /// <summary>A label's room: its kind's line and its name's, as wide as a long part's name.</summary>
    public static readonly Size LabelSize = new(154, 36);

    private static readonly string[] Kinds = ["Body", "Cockpit", "Booster"];
    // (the theme's dim and text colours, as immutable brushes: trap 15)
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Palette.Of("DimColor", Color.Parse("#BCC2BC")));
    private static readonly IBrush Text = new ImmutableSolidColorBrush(Palette.Of("TextColor", Color.Parse("#FFFFFF")));
    private static readonly IBrush Dot = Text;
    // the faint colour, three quarters strong
    private static readonly IPen Line = new ImmutablePen(new ImmutableSolidColorBrush(Palette.Of("FaintColor", Color.Parse("#9BA29B")), 0.75), 1);

    private IReadOnlyList<Point> _anchors = [];

    static PartCallouts() => AffectsRender<PartCallouts>(NamesProperty, ClearProperty);

    public double Clear
    {
        get => GetValue(ClearProperty);
        set => SetValue(ClearProperty, value);
    }

    /// <summary>Where the three labels stand (body, cockpit, booster) in a view of <paramref name="size"/>: the body
    /// low on the left, the booster low on the right, the cockpit at the right just under <paramref name="clear"/>
    /// (and never lower than above the booster's).</summary>
    public static Point[] Places(Size size, double clear)
    {
        var (w, h) = (size.Width, size.Height);
        var low = h - 52;
        var cockpit = Math.Min(clear + 8, low - LabelSize.Height - 8);
        return [new(16, low), new(w - 170, Math.Max(clear + 8, cockpit)), new(w - 170, low)];
    }

    public PartCallouts() => IsHitTestVisible = false;

    /// <summary>The three parts' names (body, cockpit, booster).</summary>
    public IReadOnlyList<string>? Names
    {
        get => GetValue(NamesProperty);
        set => SetValue(NamesProperty, value);
    }

    /// <summary>Where the three parts stand, in this control's coordinates; none: no lines.</summary>
    public IReadOnlyList<Point> Anchors
    {
        get => _anchors;
        set
        {
            _anchors = value ?? [];
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Names is not { Count: 3 } names || _anchors.Count != 3)
        {
            return;
        }
        var face = new Typeface(new FontFamily("Saira Semi Condensed"), FontStyle.Normal, FontWeight.Medium);
        var places = Places(Bounds.Size, Clear);
        for (var k = 0; k < 3; k++)
        {
            var kind = Format(Kinds[k], face, 11, Dim);
            var name = Format(names[k], face, 15, Text);
            var at = places[k];
            context.DrawText(kind, at);
            context.DrawText(name, new Point(at.X, at.Y + 14));
            // the line leaves from the label's edge nearest the part
            var anchor = _anchors[k];
            var width = Math.Max(kind.Width, name.Width);
            var from = new Point(anchor.X < at.X ? at.X - 6 : at.X + width + 6, at.Y + 16);
            var elbow = new Point(from.X + (anchor.X < from.X ? -14 : 14), from.Y);
            context.DrawLine(Line, from, elbow);
            context.DrawLine(Line, elbow, anchor);
            context.DrawEllipse(Dot, null, anchor, 2.5, 2.5);
        }
    }

    private static FormattedText Format(string text, Typeface face, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush);
}
