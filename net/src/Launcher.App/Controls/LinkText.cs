using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Launcher.Core;

namespace Launcher.App.Controls;

/// <summary>
/// A server's text with its links (<see cref="MessageLinks"/>: http and https only) underlined in the accent colour
/// (<see cref="LinkBrush"/>); a press on one opens it in the player's browser (the window's own launcher, the
/// system's default browser), and the pointer is a hand over it. The rest is plain text.
/// </summary>
public sealed class LinkText : TextBlock
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<LinkText, string?>(nameof(Message));

    public static readonly StyledProperty<IBrush?> LinkBrushProperty =
        AvaloniaProperty.Register<LinkText, IBrush?>(nameof(LinkBrush));

    private readonly List<(int Start, int End, Uri Link)> _links = [];

    static LinkText()
    {
        MessageProperty.Changed.AddClassHandler<LinkText>((t, _) => t.Compose());
        LinkBrushProperty.Changed.AddClassHandler<LinkText>((t, _) => t.Compose());
    }

    public LinkText()
    {
        TextWrapping = TextWrapping.Wrap;
        Background = Brushes.Transparent;   // the pointer is heard all over the text's box
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>The server's text.</summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>The links' colour (the accent).</summary>
    public IBrush? LinkBrush
    {
        get => GetValue(LinkBrushProperty);
        set => SetValue(LinkBrushProperty, value);
    }

    /// <summary>The links of the text now, in order.</summary>
    public IReadOnlyList<Uri> Links => [.. _links.Select(l => l.Link)];

    /// <summary>How a link is opened (tests: their own); the window's launcher otherwise.</summary>
    public Func<Uri, Task>? Open { get; set; }

    /// <summary>The link under a point of this control's box (on its letters, over every line it wraps to); null:
    /// none.</summary>
    public Uri? LinkAt(Point point)
    {
        var at = point - new Point(Padding.Left, Padding.Top);
        return _links.FirstOrDefault(l => TextLayout.HitTestTextRange(l.Start, l.End - l.Start).Any(r => r.Contains(at))).Link;
    }

    /// <summary>Where a link's first letter is drawn, in this control's box (tests press there).</summary>
    public Point? PointOf(Uri link)
    {
        if (_links.FirstOrDefault(l => l.Link == link) is not { Link: not null } found)
        {
            return null;
        }
        var box = TextLayout.HitTestTextPosition(found.Start);
        return new Point(box.X + 2 + Padding.Left, box.Y + (box.Height / 2) + Padding.Top);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = LinkAt(e.GetPosition(this)) is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left && LinkAt(e.GetPosition(this)) is { } link)
        {
            e.Handled = true;
            _ = (Open ?? Launch)(link);
        }
    }

    private async Task Launch(Uri link)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            await launcher.LaunchUriAsync(link).ConfigureAwait(true);
        }
    }

    private void Compose()
    {
        _links.Clear();
        var inlines = Inlines ??= [];
        inlines.Clear();
        var at = 0;
        foreach (var piece in MessageLinks.Split(Message))
        {
            var run = new Run(piece.Text);
            if (piece.Link is { } link)
            {
                run.Foreground = LinkBrush;
                run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
                _links.Add((at, at + piece.Text.Length, link));
            }
            inlines.Add(run);
            at += piece.Text.Length;
        }
    }
}
