using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>A page that is a list of places to go into (<see cref="MenuViewModel"/>).</summary>
public sealed partial class MenuView : UserControl
{
    public MenuView() => AvaloniaXamlLoader.Load(this);

    private void OnRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MenuRow row })
        {
            row.Open();
        }
    }
}
