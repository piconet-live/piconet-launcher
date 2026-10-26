using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>Play's Custom lobbies page: the browser, and Create.</summary>
public sealed partial class CustomLobbiesView : UserControl
{
    public CustomLobbiesView() => AvaloniaXamlLoader.Load(this);
}
