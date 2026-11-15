using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The lobby's panel: its rules, its racers and watchers, and what else a player can do in it.</summary>
public sealed partial class LobbySheetView : UserControl
{
    public LobbySheetView() => AvaloniaXamlLoader.Load(this);
}
