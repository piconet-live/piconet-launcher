using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned Stats; "More numbers" folds the other figures out (a view's own state: nothing is kept).</summary>
public sealed partial class StatsPageView : UserControl
{
    public StatsPageView() => AvaloniaXamlLoader.Load(this);

    private void OnMoreNumbers(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ItemsControl>("MoreFigures") is not { } more)
        {
            return;
        }
        more.IsVisible = !more.IsVisible;
        if (this.FindControl<TextBlock>("MoreWords") is { } words)
        {
            words.Text = more.IsVisible ? "Fewer numbers" : "More numbers";
        }
        if (this.FindControl<TextBlock>("MoreLine") is { } line)
        {
            line.IsVisible = !more.IsVisible;
        }
    }
}
