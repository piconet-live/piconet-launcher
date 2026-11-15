using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>A server's two ways onto it (<see cref="ViewModels.NextUi.ServerSheetViewModel"/>).</summary>
public sealed partial class ServerSheetView : UserControl
{
    public ServerSheetView() => AvaloniaXamlLoader.Load(this);
}
