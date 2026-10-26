using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>The Stats page (the Replays tab's second page): all of it is the view model's.</summary>
public sealed partial class StatsView : UserControl
{
    public StatsView() => AvaloniaXamlLoader.Load(this);
}
