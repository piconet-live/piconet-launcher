using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>The Play page. Its layout answers the width: wide, the map's place is 520 px with the standings beside it;
/// in a narrow window the map takes the width with the standings under it, and the tables drop their lesser columns.</summary>
public sealed partial class HomeView : UserControl
{
    /// <summary>The map's place (520), the gutter (16) and the least room for what stands beside it (200).</summary>
    private const double TwoColumns = 520 + 16 + 200;

    public HomeView()
    {
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    private void Arrange(double width)
    {
        var top = this.FindControl<Grid>("Top")!;
        var side = this.FindControl<StackPanel>("Side")!;
        var narrow = width < TwoColumns;
        Classes.Set("narrow", narrow);
        if (narrow)
        {
            top.ColumnDefinitions = new ColumnDefinitions("*,0,0");
            Grid.SetRow(side, 1);
            Grid.SetColumn(side, 0);
            side.Margin = new Avalonia.Thickness(0, 12, 0, 0);
        }
        else
        {
            top.ColumnDefinitions = new ColumnDefinitions("520,16,*");
            Grid.SetRow(side, 0);
            Grid.SetColumn(side, 2);
            side.Margin = default;
        }
    }
}
