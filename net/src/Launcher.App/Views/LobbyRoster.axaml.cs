using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>The lobby's racers and its watchers (LobbyView).</summary>
public sealed partial class LobbyRoster : UserControl
{
    public LobbyRoster() => AvaloniaXamlLoader.Load(this);
}
