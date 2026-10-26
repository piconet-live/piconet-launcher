using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Launcher.Core;

namespace Launcher.App.Views;

public sealed partial class PracticeView : UserControl
{
    // the plate's padding on either side of its name and venue line (PracticeView.axaml's styles)
    private const double AroundWide = 24 + 88;
    private const double AroundNarrow = 16 + 64;

    public PracticeView()
    {
        AvaloniaXamlLoader.Load(this);
        this.Get<Border>("Hero").PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                Fit(this.Get<Border>("Hero").Bounds.Width);
            }
        };
    }

    /// <summary>The title plate for a hero of <paramref name="width"/>: small under <see cref="CourseShot.NarrowHero"/>
    /// (the name at 40), and its name and venue line never wider than keeps the plate to
    /// <see cref="CourseShot.PlateShare"/> of the hero.</summary>
    private void Fit(double width)
    {
        if (width <= 0)
        {
            return;
        }
        var narrow = width < CourseShot.NarrowHero;
        this.Get<Border>("Hero").Classes.Set("narrow", narrow);
        var name = this.Get<TextBlock>("TrackName");
        name.Classes.Set("d64", !narrow);
        name.Classes.Set("d40", narrow);
        var room = Math.Min(520, CourseShot.NameRoom(width, narrow ? AroundNarrow : AroundWide));
        this.Get<Viewbox>("NameBox").MaxWidth = room;
        this.Get<Viewbox>("OverBox").MaxWidth = room;
    }
}
