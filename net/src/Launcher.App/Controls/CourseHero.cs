using Avalonia;
using Avalonia.Controls;
using Launcher.App.Gpu;
using Launcher.App.Theme;
using Launcher.Core;

namespace Launcher.App.Controls;

/// <summary>
/// The Practice page's hero: the picked course's model from the course select on its venue's sky, turning once in
/// 40 seconds as the game's course select turns it (<see cref="CourseShot"/>), framed so that the whole model stays
/// inside at every turn, drawn on the graphics card (<see cref="CourseHeroView"/>). The sky fills the hero; the model
/// is framed in the part right of the title plate (<see cref="Covered"/>), so the plate never hides it at any width
/// It turns only while it shows: a hidden page or window draws nothing, and the turn goes on
/// from where it stood.
/// </summary>
public sealed class CourseHero : Panel
{
    public static readonly StyledProperty<int> CourseProperty =
        AvaloniaProperty.Register<CourseHero, int>(nameof(Course), -1);

    /// <summary>How far from the hero's left the title plate covers it, in layout units.</summary>
    public static readonly StyledProperty<double> CoveredProperty =
        AvaloniaProperty.Register<CourseHero, double>(nameof(Covered));

    private readonly CourseHeroView _view = new();
    private int _asked;

    public CourseHero()
    {
        ClipToBounds = true;
        Children.Add(new Unscaled { Child = _view });   // drawn at the screen's pixels under the interface scale
    }

    public int Course
    {
        get => GetValue(CourseProperty);
        set => SetValue(CourseProperty, value);
    }

    public double Covered
    {
        get => GetValue(CoveredProperty);
        set => SetValue(CoveredProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CourseProperty)
        {
            _ = AskAsync();
        }
        else if (change.Property == CoveredProperty || change.Property == BoundsProperty)
        {
            _view.SetCovered(Bounds.Width > 0 ? Math.Clamp(Covered / Bounds.Width, 0, 1) : 0);
        }
    }

    private async Task AskAsync()
    {
        var asked = ++_asked;
        var look = Course < 0 ? null : await GameSkin.CourseLookAsync(Course);
        if (asked == _asked)
        {
            _view.SetLook(look);
        }
    }
}

/// <summary>The hero's picture on the graphics card (see <see cref="CourseHero"/>).</summary>
internal sealed class CourseHeroView : GpuView
{
    private MapPainter? _painter;
    private CourseLook? _look;
    private (CourseModel Model, PixelSize Size, CourseFrame Frame)? _framed;
    private double _covered;   // the share of the width the title plate covers
    private double _turned;   // seconds turned while shown
    private long _frame;

    public void SetLook(CourseLook? look)
    {
        _look = look;
        Redraw();
    }

    public void SetCovered(double share)
    {
        if (share != _covered)
        {
            _covered = share;
            Redraw();
        }
    }

    // turning slowly: 30 frames a second are smooth enough, at half a full frame rate's cost
    protected override double FrameMs => 1000.0 / 30;

    protected override bool Animating => _look is not null;

    protected override void Init(Gl gl) => _painter = new MapPainter(gl);

    protected override void Deinit(Gl gl, bool lost)
    {
        if (!lost)
        {
            _painter?.Delete();
        }
        _painter = null;
    }

    protected override void Draw(Gl gl, PixelSize size)
    {
        var now = Environment.TickCount64;
        // a frame after a pause (hidden, minimised) turns on from where it stood
        _turned += (_frame == 0 ? 0 : Math.Clamp(now - _frame, 0, 100)) / 1000.0;
        _frame = now;
        if (_painter is not { } painter || _look is not { } look)
        {
            return;
        }
        painter.Sky(look.Sky);
        // the model in the part right of the plate: its own viewport, framed for that part's size
        var (x, width) = CourseShot.HeroArea(size.Width, (int)Math.Round(_covered * size.Width));
        var area = new PixelSize(width, size.Height);
        var model = look.Model;
        if (_framed is not { } framed || !ReferenceEquals(framed.Model, model) || framed.Size != area)
        {
            framed = (model, area, CourseShot.Hero(model, area.Width, area.Height));
            _framed = framed;
        }
        gl.Api.Viewport(x, 0, area.Width, area.Height);
        painter.Track(model, CourseShot.HeroViewProjection(model, framed.Frame, _turned, area.Width, area.Height), look.Sky.Dark, look.Sky.Lit);
        gl.Api.Viewport(0, 0, size.Width, size.Height);
    }
}
