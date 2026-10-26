using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>A lobby in a list: one look wherever lobbies are listed (the Play page's table today).</summary>
public sealed partial class LobbyRowView : UserControl
{
    public LobbyRowView() => AvaloniaXamlLoader.Load(this);
}
