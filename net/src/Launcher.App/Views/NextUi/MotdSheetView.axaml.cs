using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The panel of the server's message of the day.</summary>
public sealed partial class MotdSheetView : UserControl
{
    public MotdSheetView() => AvaloniaXamlLoader.Load(this);
}
