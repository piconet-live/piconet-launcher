using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Launcher.App.Controls;

/// <summary>
/// A player's name with their tag after it, as players see it everywhere: "NAME #4F2A". The
/// name in this text's own look (its class: the left column's large name, a list's plain one); the tag in the dim
/// colour, a little smaller (<see cref="TagScale"/>, the theme's TagScale) and never bolder than regular. No tag (a
/// server before tags, a tag that is none: <c>ServerMessages.Tag</c>): the name alone. The tag shows only while the
/// pointer is over the name; otherwise the name has the whole room. A name too long for the
/// room it is given is cut before the tag ("ALEXANDR… #4F2A"): the tag is never the part cut off. Nothing is done on
/// the pointer but showing the tag: the keyboard misses nothing. A name that leans (the display role's skew, the left
/// column's large name) reaches past its letters' box on the right: that reach is part of this control's own box and
/// of the room the name is fitted to, so whatever holds the name (a button clips to its box) never cuts it flat
/// (a last K or W would lose its tip).
/// </summary>
public sealed class NameTagText : TextBlock
{
    public static readonly StyledProperty<string?> PlayerNameProperty =
        AvaloniaProperty.Register<NameTagText, string?>(nameof(PlayerName));

    public static readonly StyledProperty<string?> PlayerTagProperty =
        AvaloniaProperty.Register<NameTagText, string?>(nameof(PlayerTag));

    public static readonly StyledProperty<double> TagScaleProperty =
        AvaloniaProperty.Register<NameTagText, double>(nameof(TagScale), 0.8);

    public static readonly StyledProperty<IBrush?> TagBrushProperty =
        AvaloniaProperty.Register<NameTagText, IBrush?>(nameof(TagBrush));

    static NameTagText()
    {
        PlayerNameProperty.Changed.AddClassHandler<NameTagText>((t, _) => t.Compose());
        PlayerTagProperty.Changed.AddClassHandler<NameTagText>((t, _) => t.Compose());
        TagScaleProperty.Changed.AddClassHandler<NameTagText>((t, _) => t.Compose());
        TagBrushProperty.Changed.AddClassHandler<NameTagText>((t, _) => t.Compose());
        FontSizeProperty.Changed.AddClassHandler<NameTagText>((t, _) => t.Compose());
        FontWeightProperty.Changed.AddClassHandler<NameTagText>((t, _) => t.Compose());
        IsPointerOverProperty.Changed.AddClassHandler<NameTagText>((t, _) => t.Compose());
        AffectsMeasure<NameTagText>(RenderTransformProperty, RenderTransformOriginProperty, LetterSpacingProperty);
    }

    public NameTagText()
    {
        TextTrimming = TextTrimming.CharacterEllipsis;
        Background = Brushes.Transparent;   // the pointer is over the name anywhere in its box, not only on its letters
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>The name as the game shows it.</summary>
    public string? PlayerName
    {
        get => GetValue(PlayerNameProperty);
        set => SetValue(PlayerNameProperty, value);
    }

    /// <summary>The tag, without its "#"; null or blank: none.</summary>
    public string? PlayerTag
    {
        get => GetValue(PlayerTagProperty);
        set => SetValue(PlayerTagProperty, value);
    }

    /// <summary>The tag's size as a share of the name's.</summary>
    public double TagScale
    {
        get => GetValue(TagScaleProperty);
        set => SetValue(TagScaleProperty, value);
    }

    /// <summary>The tag's colour (the theme's dim text unless set).</summary>
    public IBrush? TagBrush
    {
        get => GetValue(TagBrushProperty);
        set => SetValue(TagBrushProperty, value);
    }

    /// <summary>The name and the tag as one line of text, for what is copied or read out.</summary>
    public static string Shown(string? name, string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? name ?? "" : Online.Protocol.PlayerTags.Shown(name ?? "", tag);

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Compose();   // the theme's dim colour can be found now
    }

    private Run? _name;
    private Run? _tag;

    private double _overhang;

    /// <summary>The name cut so that it and the whole tag fit the width given, with an ellipsis where it was cut; the
    /// box is the letters' and, after them, the <see cref="Overhang"/> of their lean.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        // the lean's reach depends on the box's height (one line: not on the width given), known once measured: the
        // reach last measured first, measured again only when the height brings another
        var over = _overhang;
        var size = default(Size);
        for (var pass = 0; pass < 3; pass++)
        {
            var letters = Math.Max(0, availableSize.Width - over);
            Fit(letters - Padding.Left - Padding.Right);
            size = base.MeasureOverride(availableSize.WithWidth(letters));
            var reach = Overhang(RenderTransform, RenderTransformOrigin, size);
            if (Math.Abs(reach - over) < 0.01)
            {
                break;
            }
            over = reach;
        }
        _overhang = over;
        return size.WithWidth(size.Width + over);
    }

    /// <summary>The letters laid out in the box less the lean's reach (the text's own trimming and alignment see that
    /// width); the box itself keeps the reach.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        base.ArrangeOverride(finalSize.WithWidth(Math.Max(0, finalSize.Width - _overhang)));
        return finalSize;
    }

    /// <summary>How far past its box's right edge a box is drawn under a render transform about its origin: a 12
    /// degree skew about a 40 high line's foot, the box's height × tan 12° (8.5). The box's top corner bounds every
    /// letter in it (the capitals' tops reach about 7.2 of the 8.5 in the display role's line).</summary>
    public static double Overhang(ITransform? transform, RelativePoint origin, Size box)
    {
        if (transform is null || box.Width is not (>= 0 and < double.PositiveInfinity) || !double.IsFinite(box.Height))
        {
            return 0;
        }
        var at = origin.ToPixels(box);
        var drawn = Matrix.CreateTranslation(-at.X, -at.Y) * transform.Value * Matrix.CreateTranslation(at.X, at.Y);
        var right = new[] { new Point(0, 0), new Point(box.Width, 0), new Point(0, box.Height), new Point(box.Width, box.Height) }
            .Max(p => p.Transform(drawn).X);
        return Math.Max(0, right - box.Width);
    }

    private void Fit(double width)
    {
        if (_name is null)
        {
            return;
        }
        var name = PlayerName ?? "";
        var shown = name;
        if (double.IsFinite(width))
        {
            // cut here, not by the text's own trimming, with or without a tag: whole letters and the tag kept
            var room = width - (_tag is null ? 0 : TextWidth(_tag.Text ?? "", _tag.FontSize, _tag.FontWeight));
            if (TextWidth(name, FontSize, FontWeight) > room)
            {
                // the longest beginning of the name that fits with its ellipsis (at least its first letter), cut
                // between whole letters as the reader sees them: never inside an emoji's pair of characters or a
                // letter and its accent
                var starts = System.Globalization.StringInfo.ParseCombiningCharacters(name);
                int End(int letters) => letters < starts.Length ? starts[letters] : name.Length;
                var (low, high) = (1, starts.Length - 1);
                while (low < high)
                {
                    var mid = (low + high + 1) / 2;
                    if (TextWidth(name[..End(mid)] + "…", FontSize, FontWeight) <= room)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }
                shown = starts.Length > 1 ? name[..End(low)] + "…" : name;
            }
        }
        if (_name.Text != shown)
        {
            _name.Text = shown;
        }
    }

    /// <summary>A text's width as this control lays it out: its face, size and letter spacing (the layout's own,
    /// so the fit measures what is drawn).</summary>
    private double TextWidth(string text, double size, FontWeight weight)
    {
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, new Typeface(FontFamily, FontStyle, weight, FontStretch),
            size, null, letterSpacing: LetterSpacing, fontFeatures: FontFeatures);
        return layout.WidthIncludingTrailingWhitespace;
    }

    private void Compose()
    {
        var inlines = Inlines ??= [];
        inlines.Clear();
        _name = new Run(PlayerName ?? "");
        _tag = null;
        inlines.Add(_name);
        if (string.IsNullOrWhiteSpace(PlayerTag) || !IsPointerOver)
        {
            return;
        }
        var tag = _tag = new Run($" #{PlayerTag}")
        {
            FontSize = Math.Max(1, Math.Round(FontSize * TagScale)),
            FontWeight = FontWeight > FontWeight.Medium ? FontWeight.Medium : FontWeight.Normal,
            BaselineAlignment = BaselineAlignment.Baseline,
        };
        if ((TagBrush ?? (this.TryFindResource("DimBrush", out var dim) ? dim as IBrush : null)) is { } brush)
        {
            tag.Foreground = brush;
        }
        inlines.Add(tag);
    }
}
