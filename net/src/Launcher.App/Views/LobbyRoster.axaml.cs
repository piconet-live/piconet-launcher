using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>The lobby's racers, its watchers and its chat (LobbyView).</summary>
public sealed partial class LobbyRoster : UserControl
{
    public LobbyRoster() => AvaloniaXamlLoader.Load(this);
}
