using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's machine stage: the machine selected, its grades, Ready.</summary>
public sealed partial class MachineView : UserControl
{
    public MachineView() => AvaloniaXamlLoader.Load(this);
}
