using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's lobby while it gathers: its racers, the vote to start, the vote on CPUs.</summary>
public sealed partial class GatherView : UserControl
{
    public GatherView() => AvaloniaXamlLoader.Load(this);
}
