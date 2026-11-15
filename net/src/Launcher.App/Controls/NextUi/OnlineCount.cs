using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;

namespace Launcher.App.Controls.NextUi;

/// <summary>
/// How many players are online, in the top bar beside the profile: a small figure of a person and the count (a dash
/// while it is not known, <see cref="Count"/> below 0). A change is shown, lightly: the count rolls to its new value
/// (<see cref="RollingText"/>), the figure swells once, and while the count went up a green wash comes over the figure
/// and goes (<see cref="Motion"/>: under Reduced motion the new count just stands). The figure's line takes the
/// button's colour (<c>Path.stroke</c>); the wash is the accent.
/// </summary>
public sealed class OnlineCount : StackPanel
{
    public static readonly StyledProperty<int> CountProperty = AvaloniaProperty.Register<OnlineCount, int>(nameof(Count), -1);

    /// <summary>A person: the head and the shoulders, in 24 by 24.</summary>
    private const string Person = "M12 3.4A4 4 0 1 1 11.99 3.4Z M4.2 20.8C4.2 16.3 7.6 13.7 12 13.7C16.4 13.7 19.8 16.3 19.8 20.8";

    private readonly Viewbox _figure;
    private readonly Path _wash;
    private readonly RollingText _figures;

    public OnlineCount()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 7;
        var line = new Path { Data = Geometry.Parse(Person), StrokeThickness = 2.1, StrokeLineCap = PenLineCap.Round };
        line.Classes.Add("stroke");
        _wash = new Path { Name = "Wash", Data = Geometry.Parse(Person), StrokeThickness = 2.1, StrokeLineCap = PenLineCap.Round, Opacity = 0 };
        _wash.Classes.Add("wash");
        _figure = new Viewbox
        {
            Name = "Figure",
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Canvas { Width = 24, Height = 24, Children = { line, _wash } },
        };
        _figures = new RollingText { Name = "Figures", VerticalAlignment = VerticalAlignment.Center, Text = Words(-1) };
        _figures.Classes.Add("count");
        Children.Add(_figure);
        Children.Add(_figures);
    }

    /// <summary>The players online; below 0: not known.</summary>
    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>What the count reads: "1,284", or a dash while it is not known.</summary>
    public static string Words(int count) => count < 0 ? "–" : count.ToString("N0", CultureInfo.InvariantCulture);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == CountProperty)
        {
            Changed(change.GetOldValue<int>(), change.GetNewValue<int>());
        }
    }

    private void Changed(int before, int after)
    {
        _figures.Text = Words(after);
        if (before < 0 || after < 0 || before == after || TopLevel.GetTopLevel(this) is null)
        {
            return;   // (the first answer, or none: the count just stands)
        }
        Motion.Pulse(_figure, 0.2);
        if (after > before)
        {
            Motion.Play(_wash, nameof(_wash), Motion.Emphasis * 3, null, p => _wash.Opacity = Math.Sin(Math.PI * p),
                () => _wash.Opacity = 0);
        }
    }
}
