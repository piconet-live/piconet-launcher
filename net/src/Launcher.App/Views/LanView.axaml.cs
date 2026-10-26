using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

public sealed partial class LanView : UserControl
{
    public LanView() => AvaloniaXamlLoader.Load(this);
}
