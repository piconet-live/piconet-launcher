using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's custom lobbies: the list, and Create.</summary>
public sealed partial class CustomListView : UserControl
{
    public CustomListView() => AvaloniaXamlLoader.Load(this);
}
