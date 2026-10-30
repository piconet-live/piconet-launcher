using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>A mode's lines for the race and for each team (<see cref="ViewModels.RaceViewModel"/>).</summary>
public sealed partial class RaceModeLines : UserControl
{
    public RaceModeLines() => AvaloniaXamlLoader.Load(this);
}
