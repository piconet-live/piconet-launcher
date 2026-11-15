using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's custom lobby while it gathers: its room.</summary>
public sealed partial class RoomView : UserControl
{
    public RoomView() => AvaloniaXamlLoader.Load(this);
}
