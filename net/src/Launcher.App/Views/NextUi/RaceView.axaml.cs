using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's race, as it runs and once it is over.</summary>
public sealed partial class RaceView : UserControl
{
    public RaceView() => AvaloniaXamlLoader.Load(this);
}
