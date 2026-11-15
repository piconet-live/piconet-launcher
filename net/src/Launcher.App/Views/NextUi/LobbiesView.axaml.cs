using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's Quick Play page out of a lobby: the lobbies people are in, and RACE.</summary>
public sealed partial class LobbiesView : UserControl
{
    public LobbiesView() => AvaloniaXamlLoader.Load(this);
}
