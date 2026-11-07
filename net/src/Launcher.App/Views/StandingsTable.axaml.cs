using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>The race's standings (<see cref="RaceViewModel.Standings"/>).</summary>
public sealed partial class StandingsTable : UserControl
{
    public StandingsTable()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ItemsControl>("StandingList")!.PointerPressed += OnStandingPressed;
    }

    /// <summary>A racer's line pressed: the map follows them (or stops). On the press rather than a full click: the lines
    /// refresh 10 times a second, and a click's press and release would have to land on the same line.</summary>
    private void OnStandingPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && (e.Source as StyledElement)?.DataContext is StandingRow row && DataContext is RaceViewModel race)
        {
            race.FollowCommand.Execute(row);
            e.Handled = true;
        }
    }
}
