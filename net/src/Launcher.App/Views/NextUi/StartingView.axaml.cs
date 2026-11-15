using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's lobby while a race is being started.</summary>
public sealed partial class StartingView : UserControl
{
    public StartingView() => AvaloniaXamlLoader.Load(this);
}
