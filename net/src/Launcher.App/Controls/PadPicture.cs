using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Launcher.Core;

using Launcher.App.Theme;

namespace Launcher.App.Controls;

/// <summary>
/// The Controller page's test: a GameCube controller drawn flat, lit where the game sees a control held, with both
/// sticks as a dot in their circle and both triggers as a bar, each with its value. It draws what it is given
/// (<see cref="Pad"/>: the game's own reading of the port) and knows nothing of devices or mappings.
/// </summary>
public sealed class PadPicture : Control
{
    public static readonly StyledProperty<HelperPad?> PadProperty = AvaloniaProperty.Register<PadPicture, HelperPad?>(nameof(Pad));

    // the theme's colours (Theme.axaml): Off, Faint, Text, the accent and the dark on it, Raised; read from the
    // theme's dictionary once (Theme/Palette.cs), so the accent has one place.
    // Brushes and pens of no thread (the immutable kind): they are the class's, and a brush made of an Avalonia
    // object belongs to the thread that made it, which is the window's in the launcher and one of several in the
    // tests (each headless application has its own).
    private static readonly ImmutableSolidColorBrush Line = new ImmutableSolidColorBrush(Palette.Of("OffColor", Color.Parse("#5C615C")));
    private static readonly ImmutableSolidColorBrush Faint = new ImmutableSolidColorBrush(Palette.Of("FaintColor", Color.Parse("#9BA29B")));
    private static readonly ImmutableSolidColorBrush Text = new ImmutableSolidColorBrush(Palette.Of("TextColor", Color.Parse("#FFFFFF")));
    private static readonly ImmutableSolidColorBrush Lit = new ImmutableSolidColorBrush(Palette.Of("AccentColor", Color.Parse("#3DFF6A")));
    private static readonly ImmutableSolidColorBrush OnLit = new ImmutableSolidColorBrush(Palette.Of("OnAccentColor", Color.Parse("#000000")));
    private static readonly ImmutableSolidColorBrush Off = new ImmutableSolidColorBrush(Palette.Of("RaisedColor", Color.Parse("#232623")));
    private static readonly IPen Edge = new ImmutablePen(Line, 1);
    private static readonly IPen FaintEdge = new ImmutablePen(Off, 1);

    static PadPicture() => AffectsRender<PadPicture>(PadProperty);

    public PadPicture()
    {
        Width = 320;
        Height = 256;
    }

    public HelperPad? Pad
    {
        get => GetValue(PadProperty);
        set => SetValue(PadProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pad = Pad is { Connected: true } p ? p : null;
        bool On(string button) => pad is not null && pad.Buttons.Contains(button);

        // the triggers, with Z under R
        Bar(context, new Rect(8, 6, 110, 16), "L", pad?.L ?? 0, On("L"));
        Bar(context, new Rect(202, 6, 110, 16), "R", pad?.R ?? 0, On("R"));
        Box(context, new Rect(266, 28, 46, 16), "Z", On("Z"));

        // the left side: the stick, the D-Pad under it
        Stick(context, new Point(62, 92), 34, "Stick", pad?.StickX ?? 128, pad?.StickY ?? 128, pad is not null);
        Box(context, new Rect(96, 168, 20, 20), "", On("Up"));
        Box(context, new Rect(96, 208, 20, 20), "", On("Down"));
        Box(context, new Rect(76, 188, 20, 20), "", On("Left"));
        Box(context, new Rect(116, 188, 20, 20), "", On("Right"));
        Label(context, "D-Pad", new Point(106, 232), Faint, 12, centred: true);

        Round(context, new Point(160, 96), 9, "", On("Start"));
        Label(context, "Start", new Point(160, 108), Faint, 12, centred: true);

        // the right side: A with B, X and Y around it, the C-Stick under them
        Round(context, new Point(256, 100), 22, "A", On("A"));
        Round(context, new Point(217, 121), 12, "B", On("B"));
        Round(context, new Point(297, 90), 12, "X", On("X"));
        Round(context, new Point(246, 62), 12, "Y", On("Y"));
        Stick(context, new Point(222, 186), 26, "C-Stick", pad?.CStickX ?? 128, pad?.CStickY ?? 128, pad is not null);
    }

    private void Bar(DrawingContext context, Rect box, string name, int value, bool on)
    {
        context.DrawRectangle(null, Edge, box);
        if (value > 0)
        {
            context.DrawRectangle(Lit, null, new Rect(box.X, box.Y, box.Width * value / 255.0, box.Height));
        }
        Label(context, name, new Point(box.X, box.Bottom + 2), on ? Lit : Faint, 13);
        Label(context, value.ToString(CultureInfo.InvariantCulture), new Point(box.X + 18, box.Bottom + 2), Text, 13);
    }

    private void Box(DrawingContext context, Rect box, string name, bool on)
    {
        context.DrawRectangle(on ? Lit : null, Edge, box);
        if (name.Length > 0)
        {
            Label(context, name, box.Center, on ? OnLit : Text, 13, centred: true, middle: true);
        }
    }

    private void Round(DrawingContext context, Point centre, double radius, string name, bool on)
    {
        context.DrawEllipse(on ? Lit : null, Edge, centre, radius, radius);
        if (name.Length > 0)
        {
            Label(context, name, centre, on ? OnLit : Text, 14, centred: true, middle: true);
        }
    }

    /// <summary>A stick: its circle, a cross at rest, the dot where the game sees it (0 to 255, up is more), and
    /// the two values under it.</summary>
    private void Stick(DrawingContext context, Point centre, double radius, string name, int x, int y, bool live)
    {
        context.DrawEllipse(null, Edge, centre, radius, radius);
        context.DrawLine(FaintEdge, new Point(centre.X - radius, centre.Y), new Point(centre.X + radius, centre.Y));
        context.DrawLine(FaintEdge, new Point(centre.X, centre.Y - radius), new Point(centre.X, centre.Y + radius));
        var dot = new Point(centre.X + (x - 128) / 127.0 * radius, centre.Y - (y - 128) / 127.0 * radius);
        var moved = live && (Math.Abs(x - 128) > 2 || Math.Abs(y - 128) > 2);
        context.DrawEllipse(moved ? Lit : live ? Text : Off, null, dot, 5, 5);
        Label(context, name, new Point(centre.X, centre.Y + radius + 3), Faint, 12, centred: true);
        Label(context, string.Create(CultureInfo.InvariantCulture, $"{x}, {y}"), new Point(centre.X, centre.Y + radius + 18), Text, 13, centred: true);
    }

    private void Label(DrawingContext context, string words, Point at, IBrush brush, double size, bool centred = false, bool middle = false)
    {
        var text = new FormattedText(words, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this)), size, brush);
        context.DrawText(text, new Point(centred ? at.X - text.Width / 2 : at.X, middle ? at.Y - text.Height / 2 : at.Y));
    }
}
