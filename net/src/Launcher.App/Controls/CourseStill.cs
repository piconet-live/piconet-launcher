using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Launcher.App.Gpu;

namespace Launcher.App.Controls;

/// <summary>A course's still for the Practice page's tiles: its model on its venue's sky, a picture drawn once on the
/// graphics card and kept (<see cref="CourseStillPainter"/>), filling the tile (cut at its sides or top and foot, as
/// the tile's shape asks; the model keeps a margin round it). Nothing while the picture is not there (no disc, no
/// model): the tile's own surface shows.</summary>
public sealed class CourseStill : Control
{
    public static readonly StyledProperty<int> CourseProperty =
        AvaloniaProperty.Register<CourseStill, int>(nameof(Course), -1);

    private Bitmap? _picture;
    private int _asked;

    public int Course
    {
        get => GetValue(CourseProperty);
        set => SetValue(CourseProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CourseProperty)
        {
            _ = AskAsync();
        }
    }

    private async Task AskAsync()
    {
        var asked = ++_asked;
        _picture = null;
        InvalidateVisual();
        if (Course < 0)
        {
            return;
        }
        var picture = await CourseStillPainter.Paint(Course);
        if (asked == _asked)
        {
            _picture = picture;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_picture is not { } picture || Bounds.Width < 1 || Bounds.Height < 1)
        {
            return;
        }
        // the picture over the whole tile, its middle in the tile's middle
        var size = picture.Size;
        var scale = Math.Max(Bounds.Width / size.Width, Bounds.Height / size.Height);
        var (w, h) = (Bounds.Width / scale, Bounds.Height / scale);
        context.DrawImage(picture, new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h), new Rect(Bounds.Size));
    }
}
